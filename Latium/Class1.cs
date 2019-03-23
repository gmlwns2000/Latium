using System;
using System.Linq;
using System.Collections.Generic;
using OpenCvSharp;
using Latium.Net;
using System.Threading;
using System.IO;
using System.Collections.Concurrent;

namespace Latium
{
    public class ComponentNotFoundException : Exception { }



    public class Context
    {
        public Net.Communicator Communicator => communicator;
        public IEnumerable<Component> Components => components;

        Net.Communicator communicator;
        List<Component> components = new List<Component>();

        public Context()
        {
            communicator = new Communicator();
            components.Add(communicator);
        }

        public Component GetComponent(string name)
        {
            var quary = components.Where((c) => c.Name == name);
            if (quary.Any())
                return quary.First();

            throw new ComponentNotFoundException();
        }

        public Component GetComponent(RemoteComponent cond)
        {
            var quary = components.Where((c) => cond.Is(c));
            if (quary.Any())
                return quary.First();

            throw new ComponentNotFoundException();
        }

        public void AddComponent(Component c)
        {
            c.Communication = Communicator;
            c.Context = this;
            components.Add(c);
        }

        public void RemoveComponent(Component c)
        {
            c.Communication = null;
            c.Context = null;
            components.Remove(c);
        }

        public void Init()
        {
            foreach (var c in components)
            {
                c.OnInit();
            }
        }

        public void Load()
        {
            foreach (var c in components)
            {
                c.OnLoad();
            }
        }

        public void Awake()
        {
            foreach (var c in components)
            {
                c.OnAwake();
            }
        }

        public void Pause()
        {
            foreach (var c in components)
            {
                c.OnPause();
            }
        }

        public void Shutdown()
        {
            foreach (var c in components)
            {
                c.OnShutdown();
            }
        }
    }

    public abstract class Component : IDisposable
    {
        public Context Context { get; set; }

        Net.Communicator _communication;
        public Net.Communicator Communication
        {
            get => _communication;
            set
            {
                if (value != _communication)
                {
                    _communication.RemoveCallback(this);
                }
                value = _communication;
                value?.AddCallback(this, OnRecive);
            }
        }
        public bool IsDisposed { get; protected set; }
        public string Name { get; set; }
        public string Type => GetType().ToString();
        public Guid Guid { get; set; } = new Guid();

        ~Component()
        {
            if (!IsDisposed)
            {
                Dispose();
                GC.SuppressFinalize(this);
            }
        }

        public virtual void OnInit() { }
        public virtual void OnLoad() { }
        public virtual void OnAwake() { }
        public virtual void OnPause() { }
        public virtual void OnShutdown() { }
        public virtual void OnRecive(Net.CommunicateBuffer buffer) { }
        public virtual void Dispose() { IsDisposed = true; Communication = null; }
    }

    namespace IO
    {
        namespace Camera
        {
            public abstract class ICamera : Component
            {
                public abstract event EventHandler<Mat> Captured;
                public CameraType Type;
            }

            public enum CameraType
            {
                Front, Back, Stereo
            }

            public class OcvCamera : ICamera
            {
                public override event EventHandler<Mat> Captured;
                public VideoCapture Capture;
                public int Index;
                public Thread Thread;
                public bool Broadcasting;

                RemoteComponentCollection targets;

                public OcvCamera(int index)
                {
                    Capture = new VideoCapture();
                    Index = index;
                }

                public override void OnInit()
                {
                    base.OnInit();
                    Thread = new Thread(new ParameterizedThreadStart((o) =>
                    {
                        while (Capture.IsOpened())
                        {
                            using (var frame = Capture.RetrieveMat())
                            {
                                Captured?.Invoke(this, frame);
                                Cv2.ImEncode("jpg", frame, out byte[] buffer, new ImageEncodingParam(ImwriteFlags.JpegQuality, 60));
                                Communication.AddQueue(new CommunicateBuffer(this, targets, buffer));
                            }
                        }
                    }));
                    Capture.Open(Index);
                }

                public override void OnLoad()
                {
                    base.OnLoad();

                    targets = Communication.GetComponents(new RemoteComponent("CameraRemote"));

                    Thread.Start();
                }

                public override void Dispose()
                {
                    base.Dispose();

                    Thread?.Abort();
                    Thread = null;
                }
            }

            public class CameraRemote : ICamera
            {
                public override event EventHandler<Mat> Captured;

                public CameraRemote()
                {
                    Name = "CameraRemote";
                }

                public override void OnRecive(CommunicateBuffer buffer)
                {
                    base.OnRecive(buffer);

                    var frame = Cv2.ImDecode(buffer.Buffer, ImreadModes.AnyColor);
                    Captured?.Invoke(this, frame);
                }

                public override void Dispose()
                {
                    base.Dispose();
                }
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
            public CommunicatePriority Priority { get; set; } = CommunicatePriority.Normal;
            public RemoteComponentCollection Target;
            public RemoteComponent Sender;
            public DateTime SenderTime;

            public CommunicateBuffer(Component comp, RemoteComponentCollection target, byte[] buf)
            {
                Sender = new RemoteComponent(comp);
                Target = target;
                Buffer = buf;
                SenderTime = DateTime.UtcNow;
            }

            public CommunicateBuffer(byte[] buffer)
            {
                Deserialize(buffer);
            }

            void Deserialize(byte[] buffer)
            {

            }

            public byte[] Serialize()
            {

            }
        }

        public class RemoteComponentCollection : List<RemoteComponent>
        {
            public RemoteComponentCollection()
            {
                
            }

            public RemoteComponentCollection(IEnumerable<RemoteComponent> l)
            {
                AddRange(l);
            }

            public bool Has(RemoteComponent cond)
            {
                return this.Where((c) => c.Is(cond)).Any();
            }

            public bool HasIntersection(IEnumerable<RemoteComponent> list)
            {
                return this.Intersect(list).Any();
            }
        }

        public class RemoteComponent
        {
            public RemoteContext Context { get; set; }
            public Guid Guid { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }

            public RemoteComponent()
            {

            }

            public RemoteComponent(string name)
            {
                Name = name;
            }

            public RemoteComponent(RemoteContext context, string name, string type, Guid guid)
            {
                Context = context;
                Guid = guid;
                Name = name;
                Type = type;
            }

            public RemoteComponent(Component comp) : this(null, comp.Name, comp.GetType().ToString(), comp.Guid) { }

            public bool Is(Component condition)
            {
                return Is(new RemoteComponent(condition));
            }

            public bool Is(RemoteComponent condition)
            {
                var chName = condition.Name == Name || condition.Name == null || Name == null;
                var chGuid = condition.Guid == Guid || condition.Guid == null || Guid == null;
                var chType = condition.Type == Type || condition.Type == null || Type == null;
                return chName && chGuid && chType &&
                    !(condition.Name == null && Name == null &&
                    condition.Guid == null && Guid == null &&
                    condition.Type == null && Type == null);
            }
        }

        public abstract class RemoteContext
        {
            public Communicator Communicator { get; set; }
            public IEnumerable<RemoteComponent> Components => components;
            List<RemoteComponent> components;

            public abstract event EventHandler Connected;
            public abstract event EventHandler Disconnected;
            public abstract event EventHandler<CommunicateBuffer> Recieved;

            public List<RemoteComponent> GetComponents(RemoteComponent component)
            {
                if (components == null)
                    UpdateComponents();
                return components.Where((c) => c.Name == component.Name).ToList();
            }
            public void UpdateComponents()
            {
                components = OnUpdateComponents();
            }
            protected abstract List<RemoteComponent> OnUpdateComponents();
            public abstract void Send(CommunicateBuffer buffer);
            public abstract void Connect();
        }

        public class ClientRemoteContext : RemoteContext
        {

        }

        public class ServerRemoteContext : RemoteContext
        {

        }

        public class Communicator : Component
        {
            public IEnumerable<RemoteContext> Remotes => remotes;

            protected Dictionary<Component, Action<CommunicateBuffer>> Callbacks;
            List<RemoteContext> remotes = new List<RemoteContext>();

            ConcurrentQueue<CommunicateBuffer> bufferQueue = new ConcurrentQueue<CommunicateBuffer>();
            Thread thread;

            public RemoteComponentCollection GetComponents(RemoteComponent component)
            {
                var ret = new RemoteComponentCollection();
                foreach (var remote in remotes)
                {
                    var q = remote.Components.Where((c) => c.Is(component));
                    ret.AddRange(q);
                }
                return ret;
            }

            public void AddRemote(RemoteContext c)
            {
                c.Communicator = this;
                c.Recieved += OnRecive;
                remotes.Add(c);
            }

            private void OnRecive(object sender, CommunicateBuffer e)
            {
                RunCallback(e);
            }

            public void RemoveRemote(RemoteContext c)
            {
                c.Communicator = null;
                c.Recieved -= OnRecive;
                remotes.Remove(c);
            }

            public void AddQueue(CommunicateBuffer buffer)
            {
                bufferQueue.Enqueue(buffer);
            }

            public void RemoveCallback(Component component)
            {
                Callbacks.Remove(component);
            }

            public void AddCallback(Component component, Action<CommunicateBuffer> callback)
            {
                Callbacks.Add(component, callback);
            }

            protected virtual void RunCallback(CommunicateBuffer buffer)
            {
                var q = Callbacks.Keys.Where((c) => buffer.Target.Has(new RemoteComponent(c)));
                if (q.Any())
                    Callbacks[q.First()].Invoke(buffer);
            }

            public override void OnInit()
            {
                base.OnInit();
                thread = new Thread(new ParameterizedThreadStart((o) =>
                {
                    while (true)
                    {
                        if(bufferQueue.TryDequeue(out CommunicateBuffer buffer))
                        {
                            foreach (var remote in remotes)
                            {
                                if (buffer.Target.HasIntersection(remote.Components))
                                    remote.Send(buffer);
                            }
                        }
                        if(bufferQueue.Count == 0)
                            Thread.Sleep(1);
                    }
                }));
            }
        }
    }
}