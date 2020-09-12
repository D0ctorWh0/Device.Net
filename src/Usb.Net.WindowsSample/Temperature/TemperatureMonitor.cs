﻿using Device.Net;
using Hid.Net.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Usb.Net.WindowsSample.Temperature
{
    /// <summary>
    /// https://docs.microsoft.com/en-us/dotnet/standard/events/how-to-implement-a-provider#example
    /// </summary>
    public class TemperatureMonitor : IObservable<Temperature>
    {
        private readonly List<IObserver<Temperature>> observers;
        private readonly IDeviceManager _DeviceManager;
        private IDevice _device;
        private decimal? temp;
        private readonly ILoggerFactory _loggerFactory;

        public TemperatureMonitor(ILoggerFactory loggerFactory)
        {
            _DeviceManager = new DeviceManager(loggerFactory);
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            observers = new List<IObserver<Temperature>>();
            InitializeAsync().Wait();
        }

        private async Task InitializeAsync()
        {
            var windowsHidDeviceFactory = new FilterDeviceDefinition { VendorId = 0x413d, ProductId = 0x2107, UsagePage = 65280 }.CreateWindowsHidDeviceFactory(_loggerFactory);
            _DeviceManager.RegisterDeviceFactory(windowsHidDeviceFactory);
            var devices = (await _DeviceManager.GetConnectedDeviceDefinitionsAsync()).ToList();
            _device = await windowsHidDeviceFactory.GetDevice(devices.First());
            await _device.InitializeAsync();
        }

        private class Unsubscriber : IDisposable
        {
            private readonly List<IObserver<Temperature>> _observers;
            private readonly IObserver<Temperature> _observer;

            public Unsubscriber(List<IObserver<Temperature>> observers, IObserver<Temperature> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        public IDisposable Subscribe(IObserver<Temperature> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);

            return new Unsubscriber(observers, observer);
        }

        private async Task GetTemperatures()
        {
            //Thanks to https://github.com/WozSoftware
            //https://github.com/WozSoftware/Woz.TEMPer/blob/dcd0b49d67ac39d10c3759519050915816c2cd93/Woz.TEMPer/Sensors/TEMPerV14.cs#L15

            var buffer = new byte[9] { 0x00, 0x01, 0x80, 0x33, 0x01, 0x00, 0x00, 0x00, 0x00 };

            var data = await _device.WriteAndReadAsync(buffer);
            var temperatureTimesOneHundred = (data.Data[4] & 0xFF) + (data.Data[3] << 8);

            //TODO: Get the humidity

            //Note sometimes the divisor is 256...
            //https://github.com/ccwienk/temper/blob/600755de6b9ccd8d481c4844fa08185acd13aef0/temper.py#L113
            temp = Math.Round(temperatureTimesOneHundred / 100.0m, 2, MidpointRounding.ToEven);
        }

        public void GetTemperature()
        {
            // Store the previous temperature, so notification is only sent after at least .1 change.
            decimal? previous = null;
            var start = true;

            GetTemperatures().Wait();

            if (temp.HasValue)
            {
                if (start || (Math.Abs(temp.Value - previous.Value) >= 0.1m))
                {
                    var tempData = new Temperature(temp.Value, DateTime.Now);
                    foreach (var observer in observers)
                        observer.OnNext(tempData);
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    previous = temp;
                    if (start) start = false;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                }
            }
            else
            {
                foreach (var observer in observers.ToArray())
                    if (observer != null) observer.OnCompleted();

                observers.Clear();
            }
        }

    }

}
