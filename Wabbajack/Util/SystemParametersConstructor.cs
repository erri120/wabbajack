﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using PInvoke;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;
using Wabbajack.Common;
using Wabbajack.Lib;
using static PInvoke.User32;

namespace Wabbajack.Util
{
    // Much of the GDI code here is taken from : https://github.com/ModOrganizer2/modorganizer/blob/master/src/envmetrics.cpp
    // Thanks to MO2 for being good citizens and supporting OSS code
    public static class SystemParametersConstructor
    {
        private static IEnumerable<(int Width, int Height, bool IsPrimary)> GetDisplays()
        {
            // Needed to make sure we get the right values from this call
            SetProcessDPIAware();
            unsafe
            {

                var col = new List<(int Width, int Height, bool IsPrimary)>();

                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
                    delegate(IntPtr hMonitor, IntPtr hdcMonitor, RECT* lprcMonitor, void *dwData)
                    {
                        MONITORINFOEX mi = new MONITORINFOEX();
                        mi.cbSize = Marshal.SizeOf(mi);
                        bool success = GetMonitorInfo(hMonitor, (MONITORINFO*)&mi);
                        if (success)
                        {
                            col.Add(((mi.Monitor.right - mi.Monitor.left), (mi.Monitor.bottom - mi.Monitor.top),  mi.Flags == MONITORINFO_Flags.MONITORINFOF_PRIMARY));
                        }

                        return true;
                    }, IntPtr.Zero);
                return col;
            }
        }
        
        public static SystemParameters Create()
        {
            var (width, height, _) = GetDisplays().First(d => d.IsPrimary);

            /*using var f = new SharpDX.DXGI.Factory1();
            var video_memory = f.Adapters1.Select(a =>
                Math.Max(a.Description.DedicatedSystemMemory, (long)a.Description.DedicatedVideoMemory)).Max();*/

            var dxgiMemory = 0UL;
            
            unsafe
            {
                using var api = DXGI.GetApi();
                
                IDXGIFactory1* factory1 = default;
                
                try
                {
                    //https://docs.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-createdxgifactory1
                    SilkMarshal.ThrowHResult(api.CreateDXGIFactory1(SilkMarshal.GuidPtrOf<IDXGIFactory1>(), (void**)&factory1));
                    
                    uint i = 0u;
                    while (true)
                    {
                        IDXGIAdapter1* adapter1 = default;
                        
                        //https://docs.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1
                        var res = factory1->EnumAdapters1(i, &adapter1);
                        
                        var exception = Marshal.GetExceptionForHR(res);
                        if (exception != null) break;

                        AdapterDesc1 adapterDesc = default;
                        
                        //https://docs.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiadapter1-getdesc1
                        SilkMarshal.ThrowHResult(adapter1->GetDesc1(&adapterDesc));
                        
                        var systemMemory = (ulong) adapterDesc.DedicatedSystemMemory;
                        var videoMemory = (ulong) adapterDesc.DedicatedVideoMemory;
                        
                        var maxMemory = Math.Max(systemMemory, videoMemory);
                        if (maxMemory > dxgiMemory)
                            dxgiMemory = maxMemory;
                        
                        adapter1->Release();
                        i++;
                    }
                }
                catch (Exception e)
                {
                    Utils.ErrorThrow(e);
                }
                finally
                {
                    
                    if (factory1->LpVtbl != (void**)IntPtr.Zero)
                        factory1->Release();
                }
            }
            
            var memory = Utils.GetMemoryStatus();
            return new SystemParameters
            {
                ScreenWidth = width,
                ScreenHeight = height,
                VideoMemorySize = (long)dxgiMemory,
                SystemMemorySize = (long)memory.ullTotalPhys,
                SystemPageSize = (long)memory.ullTotalPageFile - (long)memory.ullTotalPhys
            };
        }
    }
}
