using System;
using System.Linq;
using System.Collections.Generic;
using OpenCvSharp;

namespace Latium
{
    //manage component
    public class Context
    {
        public Net.Communicator Communicator => _communicator;
        public IEnumerable<Component> Components => _components;

        Net.Communicator _communicator;
        List<Component> _components = new List<Component>();

        public Context(Net.Communicator communicator)
        {
            _communicator = communicator;
        }

        public void Load()
        {

        }

        public void Awake()
        {

        }

        public void Pause()
        {

        }

        public void Shutdown()
        {

        }
    }

    public abstract class Component : IDisposable
    {
        Net.Communicator _communication;
        public Net.Communicator Communication
        {
            get => _communication;
            set
            {
                if(value != _communication)
                {
                    _communication.RemoveCallback();
                }
                value = _communication;
                value?.AddCallback()
            }
        }
        public bool IsDisposed { get; protected set; }

        ~Component()
        {
            if (!IsDisposed)
            {
                Dispose();
                GC.SuppressFinalize(this);
            }
        }

        public string Name { get; set; }

        public virtual void OnLoad() { }
        public virtual void OnAwake() { }
        public virtual void OnPause() { }
        public virtual void OnShutdown() { }
        public virtual void OnRecive(Net.CommunicateBuffer buffer) { }
        public virtual void Dispose() { IsDisposed = true; }
    }

    namespace IO
    {
        namespace Camera
        {
            public abstract class ICamera : Component
            {
                public event EventHandler<Mat> Captured;
            }

            public class CameraClient : ICamera
            {
                public override void OnLoad()
                {
                    throw new NotImplementedException();
                }

                public override void Dispose()
                {
                    throw new NotImplementedException();
                }
            }

            public class CameraRemote : ICamera
            {

            }
        }
    }

    namespace Net
    {
        public enum CommunicatePriority
        {
            Normal,
            High,
        }

        public class CommunicateBuffer
        {
            public byte[] Buffer { get; set; }
            public CommunicatePriority Priority { get; set; }
            public string TargetName;

            public CommunicateBuffer(byte[] buffer)
            {

            }
        }

        public abstract class Communicator
        {
            public Context Context { get; set; }

            public abstract void AddQueue(byte[] buffer);
            protected Dictionary<string, Action<CommunicateBuffer>> Callbacks;

            public virtual void AddCallback(Component component, Action<CommunicateBuffer> callback)
            {
                Callbacks.Add(component.Name, callback);
            }

            protected virtual void RunCallback(string name, CommunicateBuffer buffer)
            {
                if (Callbacks.ContainsKey(name))
                    Callbacks[name](buffer);
            }
        }
    }
}

namespace Latium.Client
{

}

namespace Latium.Server
{

}
