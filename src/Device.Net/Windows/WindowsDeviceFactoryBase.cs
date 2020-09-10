﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Device.Net.Windows
{
    /// <summary>
    /// TODO: Merge this factory class with other factory classes. I.e. create a DeviceFactoryBase class
    /// </summary>
    public abstract class WindowsDeviceFactoryBase
    {
        #region Protected Properties
        protected ILogger Logger { get; }
        protected ILoggerFactory LoggerFactory { get; }
        #endregion

        #region Public Abstract Properties
        public abstract DeviceType DeviceType { get; }
        #endregion

        #region Protected Abstract Methods
        protected abstract ConnectedDeviceDefinition GetDeviceDefinition(string deviceId);
        protected abstract Guid GetClassGuid();
        #endregion

        #region Constructor
        /// <summary>
        /// 
        /// </summary>
        /// <param name="loggerFactory">The factory for creating new loggers for each device</param>
        /// <param name="logger">The logger that this base class will use. The generic type should come from the inheriting class</param>
        /// 
        protected WindowsDeviceFactoryBase(
            ILoggerFactory loggerFactory,
            ILogger logger)
        {
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Logger = logger;
        }
        #endregion

        #region Public Methods
        public async Task<IEnumerable<ConnectedDeviceDefinition>> GetConnectedDeviceDefinitionsAsync(FilterDeviceDefinition filterDeviceDefinition)
        {
            return await Task.Run<IEnumerable<ConnectedDeviceDefinition>>(() =>
            {
                IDisposable loggerScope = null;

                try
                {
                    loggerScope = Logger?.BeginScope("Filter Device Definition: {filterDeviceDefinition}", new object[] { filterDeviceDefinition?.ToString() });

                    var deviceDefinitions = new Collection<ConnectedDeviceDefinition>();
                    var spDeviceInterfaceData = new SpDeviceInterfaceData();
                    var spDeviceInfoData = new SpDeviceInfoData();
                    var spDeviceInterfaceDetailData = new SpDeviceInterfaceDetailData();
                    spDeviceInterfaceData.CbSize = (uint)Marshal.SizeOf(spDeviceInterfaceData);
                    spDeviceInfoData.CbSize = (uint)Marshal.SizeOf(spDeviceInfoData);
                    string productIdHex = null;
                    string vendorHex = null;

                    var guidString = GetClassGuid().ToString();
                    var copyOfClassGuid = new Guid(guidString);
                    const int flags = APICalls.DigcfDeviceinterface | APICalls.DigcfPresent;

                    Logger?.LogDebug("About to call {call} for class Guid {guidString}. Flags: {flags}", nameof(APICalls.SetupDiGetClassDevs), guidString, flags);

                    var devicesHandle = APICalls.SetupDiGetClassDevs(ref copyOfClassGuid, IntPtr.Zero, IntPtr.Zero, flags);

                    spDeviceInterfaceDetailData.CbSize = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize;

                    var i = -1;

                    if (filterDeviceDefinition != null)
                    {
                        if (filterDeviceDefinition.ProductId.HasValue) productIdHex = Helpers.GetHex(filterDeviceDefinition.ProductId);
                        if (filterDeviceDefinition.VendorId.HasValue) vendorHex = Helpers.GetHex(filterDeviceDefinition.VendorId);
                    }

                    while (true)
                    {
                        try
                        {
                            i++;

                            var isSuccess = APICalls.SetupDiEnumDeviceInterfaces(devicesHandle, IntPtr.Zero, ref copyOfClassGuid, (uint)i, ref spDeviceInterfaceData);
                            if (!isSuccess)
                            {
                                var errorCode = Marshal.GetLastWin32Error();

                                if (errorCode == APICalls.ERROR_NO_MORE_ITEMS)
                                {
                                    Logger?.LogDebug("The call to " + nameof(APICalls.SetupDiEnumDeviceInterfaces) + "  returned ERROR_NO_MORE_ITEMS");
                                    break;
                                }

                                if (errorCode > 0)
                                {
                                    Logger?.LogWarning("{call} called successfully but a device was skipped while enumerating because something went wrong. The device was at index {index}. The error code was {errorCode}.", nameof(APICalls.SetupDiEnumDeviceInterfaces), i, errorCode);
                                }
                            }

                            isSuccess = APICalls.SetupDiGetDeviceInterfaceDetail(devicesHandle, ref spDeviceInterfaceData, ref spDeviceInterfaceDetailData, 256, out _, ref spDeviceInfoData);
                            if (!isSuccess)
                            {
                                var errorCode = Marshal.GetLastWin32Error();

                                if (errorCode == APICalls.ERROR_NO_MORE_ITEMS)
                                {
                                    Logger?.LogDebug("The call to {call} returned ERROR_NO_MORE_ITEMS", new object[] { nameof(APICalls.SetupDiEnumDeviceInterfaces) });
                                    //TODO: This probably can't happen but leaving this here because there was some strange behaviour
                                    break;
                                }

                                if (errorCode > 0)
                                {
                                    Logger?.LogWarning("{nameof(APICalls.SetupDiGetDeviceInterfaceDetail)} called successfully but a device was skipped while enumerating because something went wrong. The device was at index {i}. The error code was {errorCode}.", nameof(APICalls.SetupDiEnumDeviceInterfaces), i, errorCode);
                                }
                            }

                            //Note this is a bit nasty but we can filter Vid and Pid this way I think...
                            if (filterDeviceDefinition != null)
                            {
                                if (filterDeviceDefinition.VendorId.HasValue && !spDeviceInterfaceDetailData.DevicePath.ContainsIgnoreCase(vendorHex)) continue;
                                if (filterDeviceDefinition.ProductId.HasValue && !spDeviceInterfaceDetailData.DevicePath.ContainsIgnoreCase(productIdHex)) continue;
                            }

                            var connectedDeviceDefinition = GetDeviceDefinition(spDeviceInterfaceDetailData.DevicePath);

                            if (connectedDeviceDefinition == null)
                            {
                                Logger?.LogWarning("Device with path {devicePath} was skipped. Area: {area} See previous logs.", spDeviceInterfaceDetailData.DevicePath, GetType().Name);
                                continue;
                            }

                            if (!DeviceManager.IsDefinitionMatch(filterDeviceDefinition, connectedDeviceDefinition)) continue;

                            deviceDefinitions.Add(connectedDeviceDefinition);
                        }
#pragma warning disable CA1031
                        catch (Exception ex)
                        {
                            //Log and move on
                            Logger?.LogError(ex, ex.Message);
                        }
#pragma warning restore CA1031
                    }

                    APICalls.SetupDiDestroyDeviceInfoList(devicesHandle);

                    return deviceDefinitions;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error calling " + nameof(GetConnectedDeviceDefinitionsAsync) + " region:{region}", new object[] { nameof(WindowsDeviceFactoryBase) });
                    throw;
                }
                finally
                {
                    loggerScope?.Dispose();
                }
            });
        }
        #endregion
    }
}
