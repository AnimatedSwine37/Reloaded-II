﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Reloaded.Mod.Loader.IO.Config;
using Reloaded.Mod.Loader.Server;
using Reloaded.WPF.Utilities;

namespace Reloaded.Mod.Launcher.Utility
{
    /// <summary>
    /// Injects Reloaded into an active process.
    /// </summary>
    public class ApplicationInjector
    {
        private static XamlResource<int> _xamlModLoaderSetupTimeout     = new XamlResource<int>("AppLauncherModLoaderSetupTimeout");
        private static XamlResource<int> _xamlModLoaderSetupSleepTime   = new XamlResource<int>("AppLauncherModLoaderSetupSleepTime");

        static ApplicationInjector()
        {
            _xamlModLoaderSetupTimeout.DefaultValue = 30000;
            _xamlModLoaderSetupSleepTime.DefaultValue = 32;
        }

        private Process _process;
        private BasicDllInjector _injector;

        public ApplicationInjector(Process process)
        {
            _process  = process;
            _injector = new BasicDllInjector(process);
        }

        /// <summary>
        /// Injects the Reloaded bootstrapper into an active process.
        /// </summary>
        /// <exception cref="ArgumentException">DLL Injection failed, likely due to bad DLL or application.</exception>
        public void Inject()
        {
            long handle = _injector.Inject(GetBootstrapperPath(_process));
            if (handle == 0)
                throw new ArgumentException(Errors.DllInjectionFailed());

            // Wait until mod loader loads.
            // If debugging, ignore timeout.
            bool WhileCondition()
            {
                if (CheckRemoteDebuggerPresent(_process.Handle, out var isDebuggerPresent))
                    return isDebuggerPresent;

                return false;
            }

            ActionWrappers.TryGetValueWhile(() =>
            {
                // Exit if application crashes while loading Reloaded..
                if (_process.HasExited)
                    return 0;

                var port = Client.GetPort((int)_process.Id);
                if (port == 0)
                    throw new Exception("Reloaded is still loading.");

                return port;
            }, WhileCondition, _xamlModLoaderSetupTimeout.Get(), _xamlModLoaderSetupSleepTime.Get());
        }

        private string GetBootstrapperPath(Process process)
        {
            var config = IoC.Get<LoaderConfig>();
            return process.Is64Bit() ? config.Bootstrapper64Path : config.Bootstrapper32Path;
        }

        /* Native Imports */
        [DllImport("Kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);
    }
}
