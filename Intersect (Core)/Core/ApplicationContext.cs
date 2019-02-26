﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Intersect.Logging;
using Intersect.Threading;
using JetBrains.Annotations;

namespace Intersect.Core
{
    public abstract class ApplicationContext<TContext> : IApplicationContext where TContext : ApplicationContext<TContext>
    {
        [NotNull] private object mShutdownLock;

        private bool mNeedsLockPulse;

        private bool mIsRunning;

        #region Instance Management

        [NotNull]
        private TContext This => this as TContext ?? throw new InvalidOperationException();

        [NotNull]
        private static ConcurrentInstance<TContext> ConcurrentInstance { get; }

        [NotNull]
        public static TContext Instance => ConcurrentInstance;

        static ApplicationContext()
        {
            ConcurrentInstance = new ConcurrentInstance<TContext>();
        }

        #endregion

        protected bool IsDisposing { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool IsStarted { get; private set; }

        public bool IsRunning
        {
            get => mIsRunning && !IsShutdownRequested;
            private set => mIsRunning = value;
        }

        public bool IsShutdownRequested { get; private set; }

        protected ApplicationContext()
        {
            ConcurrentInstance.Set(This);
            mShutdownLock = new object();
        }

        public void Start(bool lockUntilShutdown = true)
        {
            IsStarted = true;

            IsRunning = true;

            InternalStart();

            mNeedsLockPulse = lockUntilShutdown;

            if (!mNeedsLockPulse)
            {
                return;
            }

            lock (mShutdownLock)
            {
                Monitor.Wait(mShutdownLock);
                Log.Diagnostic("Application context exited.");
            }
        }

        public LockingActionQueue StartWithActionQueue()
        {
            Start(false);

            mNeedsLockPulse = true;

            return new LockingActionQueue(mShutdownLock);
        }

        protected abstract void InternalStart();

        public void RequestShutdown(bool join = false)
        {
            lock (this)
            {
                if (IsDisposed || IsDisposing || IsShutdownRequested)
                {
                    return;
                }

                IsShutdownRequested = true;
                var disposeTask = Task.Run(() =>
                {
                    Dispose();

                    lock (mShutdownLock)
                    {
                        Monitor.PulseAll(mShutdownLock);
                    }
                });

                if (join)
                {
                    disposeTask.Wait();
                }
            }
        }

        #region Dispose

        public void Dispose()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(typeof(TContext).Name);
            }

            lock (this)
            {
                if (IsDisposing)
                {
                    return;
                }

                IsDisposing = true;
            }

            IsRunning = false;

            ConcurrentInstance.ClearWith(This, InternalDispose);
        }

        private void InternalDispose()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            Log.Info($@"Beginning context dispose. ({stopwatch.ElapsedMilliseconds}ms)");
            Dispose(true);
            Log.Info($@"GC.SuppressFinalize ({stopwatch.ElapsedMilliseconds}ms)");
            GC.SuppressFinalize(this);
            Log.Info($@"InternalDispose() completed. ({stopwatch.ElapsedMilliseconds}ms)");

            IsDisposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Do nothing currently
            }
        }

        #endregion
    }
}
