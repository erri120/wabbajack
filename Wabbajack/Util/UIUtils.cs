﻿using DynamicData;
using DynamicData.Binding;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI;
using System;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Wabbajack.Common;

namespace Wabbajack
{
    public static class UIUtils
    {
        public static BitmapImage BitmapImageFromResource(string name) => BitmapImageFromStream(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Wabbajack;component/" + name)).Stream);

        public static BitmapImage BitmapImageFromStream(Stream stream)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static bool TryGetBitmapImageFromFile(AbsolutePath path, out BitmapImage bitmapImage)
        {
            try
            {
                if (!path.Exists)
                {
                    bitmapImage = default;
                    return false;
                }
                bitmapImage = new BitmapImage(new Uri((string)path, UriKind.RelativeOrAbsolute));
                return true;
            }
            catch (Exception)
            {
                bitmapImage = default;
                return false;
            }
        }

        public static AbsolutePath OpenFileDialog(string filter, string initialDirectory = null)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = filter;
            ofd.InitialDirectory = initialDirectory;
            if (ofd.ShowDialog() == DialogResult.OK)
                return (AbsolutePath)ofd.FileName;
            return default;
        }

        public static IDisposable BindCpuStatus(IObservable<CPUStatus> status, ObservableCollectionExtended<CPUDisplayVM> list)
        {
            return status.ObserveOn(RxApp.TaskpoolScheduler)
                .ToObservableChangeSet(x => x.ID)
                .Batch(TimeSpan.FromMilliseconds(50), RxApp.TaskpoolScheduler)
                .EnsureUniqueChanges()
                .ObserveOnGuiThread()
                .TransformAndCache(
                    onAdded: (key, cpu) => new CPUDisplayVM(cpu),
                    onUpdated: (change, vm) => vm.AbsorbStatus(change.Current))
                .AutoRefresh(x => x.IsWorking)
                .AutoRefresh(x => x.StartTime)
                .Filter(i => i.IsWorking && i.ID != WorkQueue.UnassignedCpuId)
                .Sort(SortExpressionComparer<CPUDisplayVM>.Ascending(s => s.StartTime))
                .Bind(list)
                .Subscribe();
        }

        public static IObservable<BitmapImage> DownloadBitmapImage(this IObservable<string> obs, Action<Exception> exceptionHandler)
        {
            return obs
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectTask(async url =>
                {
                    try
                    {
                        var (found, mstream) = await FindCachedImage(url);
                        if (found) return mstream;
                        
                        var ret = new MemoryStream();
                        using (var client = new HttpClient())
                        await using (var stream = await client.GetStreamAsync(url))
                        {
                            await stream.CopyToAsync(ret);
                        }

                        ret.Seek(0, SeekOrigin.Begin);

                        await WriteCachedImage(url, ret.ToArray());
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        exceptionHandler(ex);
                        return default;
                    }
                })
                .Select(memStream =>
                {
                    if (memStream == null) return default;
                    try
                    {
                        return BitmapImageFromStream(memStream);
                    }
                    catch (Exception ex)
                    {
                        exceptionHandler(ex);
                        return default;
                    }
                    finally
                    {
                        memStream.Dispose();
                    }
                })
                .ObserveOnGuiThread();
        }

        private static async Task WriteCachedImage(string url, byte[] data)
        {
            var folder = Consts.LocalAppDataPath.Combine("ModListImages");
            if (!folder.Exists) folder.CreateDirectory();
            
            var path = folder.Combine(Encoding.UTF8.GetBytes(url).xxHash().ToHex());
            await path.WriteAllBytesAsync(data);
        }

        private static async Task<(bool Found, MemoryStream data)> FindCachedImage(string uri)
        {
            var folder = Consts.LocalAppDataPath.Combine("ModListImages");
            if (!folder.Exists) folder.CreateDirectory();
            
            var path = folder.Combine(Encoding.UTF8.GetBytes(uri).xxHash().ToHex());
            return path.Exists ? (true, new MemoryStream(await path.ReadAllBytesAsync())) : (false, default);
        }

        /// <summary>
        /// Format bytes to a greater unit
        /// </summary>
        /// <param name="bytes">number of bytes</param>
        /// <returns></returns>
        public static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }
    }
}
