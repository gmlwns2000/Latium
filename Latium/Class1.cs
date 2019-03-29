using Latium.Net;
using MessagePack;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            AddComponent(communicator);
        }

        public Component GetComponent(string name)
        {
            var quary = components.Where((c) => c.Name == name);
            if (quary.Any())
                return quary.First();

            throw new ComponentNotFoundException();
        }

        public List<Component> GetComponents(RemoteComponent cond)
        {
            var quary = components.Where((c) => new RemoteComponent(c).In(cond));
            if (quary.Any())
                return quary.ToList();

            throw new ComponentNotFoundException();
        }

        public void AddComponent(Component c)
        {
            c.Communicator = Communicator;
            c.Context = this;
            components.Add(c);
        }

        public void RemoveComponent(Component c)
        {
            c.Communicator = null;
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

        Net.Communicator communicator;
        public Net.Communicator Communicator
        {
            get => communicator;
            set
            {
                if (communicator != null && value != communicator)
                {
                    communicator.RemoveCallback(this);
                }
                communicator = value;
                value?.AddCallback(this, OnRecive);
            }
        }
        public bool IsDisposed { get; protected set; }
        public string Name { get; set; }
        public string Type => GetType().FullName;
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
        public virtual void Dispose() { IsDisposed = true; Communicator = null; }
    }

    namespace IO
    {
        namespace Camera
        {
            public abstract class ICamera : Component
            {
                public abstract event EventHandler<Mat> Captured;
                public CameraType CameraType;
            }

            public enum CameraType
            {
                Front, Back, Stereo
            }

            public class OcvCamera : ICamera
            {
                public override event EventHandler<Mat> Captured;
                public VideoCapture Capture { get; set; }
                public int Index { get; set; }
                public bool Broadcasting { get; set; }

                Thread thread;

                List<RemoteComponent> targets;

                public OcvCamera(int index)
                {
                    Capture = new VideoCapture();
                    Capture.FrameWidth = 1280;
                    Capture.FrameHeight = 1080;
                    Index = index;
                }

                public override void OnInit()
                {
                    base.OnInit();
                    thread = new Thread(new ParameterizedThreadStart((o) =>
                    {
                        Proc();
                    }));
                    Capture.Open(Index);
                }

                void Proc()
                {
                    while (Capture.IsOpened())
                    {
                        using (var frame = Capture.RetrieveMat())
                        {
                            Captured?.Invoke(this, frame);
                            if (Broadcasting)
                            {
                                Cv2.ImEncode(".jpeg", frame, out byte[] buffer, new ImageEncodingParam(ImwriteFlags.JpegQuality, 40));
                                Communicator.Enqueue(new CommunicateBuffer(this, targets, buffer));
                            }
                            Thread.Sleep(1);
                        }
                    }
                }

                public override void OnLoad()
                {
                    base.OnLoad();

                    targets = Communicator.GetComponents(new RemoteComponent("CameraRemote"));
                    if (targets.Count > 0)
                        Broadcasting = true;

                    thread.Start();
                }

                public override void Dispose()
                {
                    base.Dispose();

                    thread?.Abort();
                    thread = null;
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

        [MessagePack.MessagePackObject]
        public class CommunicateBuffer
        {
            [Key(0)]
            public CommunicatePriority Priority { get; set; } = CommunicatePriority.Normal;

            [Key(1)]
            public List<RemoteComponent> Targets { get; set; }

            [Key(2)]
            public RemoteComponent Sender { get; set; }

            [Key(3)]
            public DateTime SenderTime { get; set; }

            [Key(4)]
            public byte[] Buffer { get; set; }

            public CommunicateBuffer()
            {

            }

            public CommunicateBuffer(RemoteComponent sender, List<RemoteComponent> targets, byte[] buf)
            {
                Sender = sender;
                Targets = targets;
                Buffer = buf;
                SenderTime = DateTime.UtcNow;
            }

            public CommunicateBuffer(Component sender, List<RemoteComponent> targets, byte[] buf) :
                this(new RemoteComponent(sender), targets, buf)
            {

            }

            public CommunicateBuffer(byte[] buffer)
            {
                Deserialize(buffer);
            }

            void Deserialize(byte[] buffer)
            {
                var obj = MessagePackSerializer.Deserialize<CommunicateBuffer>(buffer);
                Buffer = obj.Buffer;
                Priority = obj.Priority;
                Targets = obj.Targets;
                Sender = obj.Sender;
                SenderTime = obj.SenderTime;
            }

            public byte[] Serialize()
            {
                return MessagePackSerializer.Serialize(this);
            }
        }

        [MessagePackObject]
        public class RemoteComponent
        {
            [IgnoreMember]
            public RemoteContext Context { get; set; }
            [Key(0)]
            public Guid Guid { get; set; }
            [Key(1)]
            public string Name { get; set; }
            [Key(2)]
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

            public RemoteComponent(Component comp) : this(null, comp.Name, comp.GetType().FullName, comp.Guid) { }

            public bool In(RemoteComponent condition)
            {
                bool chName = true, chType = true, chGuid = true;
                if (condition.Name != null)
                    chName = condition.Name == Name;
                if (condition.Type != null)
                    chType = condition.Type == Type;
                if (condition.Guid != Guid.Empty)
                    chGuid = condition.Guid == Guid;
                return chName && chType && chGuid;
            }

            public override string ToString()
            {
                return $"({Type})\"{Name}\":{Guid}";
            }
        }

        public abstract class RemoteContext : IDisposable
        {
            public Communicator Communicator { get; set; }
            public IEnumerable<RemoteComponent> Components
            {
                protected set {
                    if (value != null)
                    {
                        foreach (var i in value)
                            i.Context = this;
                        components = (List<RemoteComponent>)value;
                    }
                    else
                    {
                        components = null;
                    }
                }
                get {
                    if (components == null)
                        UpdateComponents();
                    return components;
                }
            }
            public bool IsConnected { get => isConnected; set {
                    if(value != isConnected)
                    {
                        isConnected = value;
                        if (isConnected)
                            Connected?.Invoke(this, null);
                        else
                            Disconnected?.Invoke(this, null);
                    }
                }
            }
            bool isConnected = false;
            List<RemoteComponent> components;

            protected ConcurrentQueue<(CommunicateBuffer, Waiter)> BufferQueue { get; private set; } =
                new ConcurrentQueue<(CommunicateBuffer, Waiter)>();

            public event EventHandler Connected;
            public event EventHandler Disconnected;
            public event EventHandler<CommunicateBuffer> Received;

            object locker = new object();
            bool waitUpdate = false;

            public List<RemoteComponent> GetComponents(RemoteComponent cond)
            {
                if (components == null)
                    UpdateComponents();
                return components.Where((c) => c.In(cond)).ToList();
            }

            public void UpdateComponents()
            {
                components = OnUpdateComponents();
                Logger.Log(this, components);
            }

            protected virtual List<RemoteComponent> OnUpdateComponents()
            {
                if (waitUpdate)
                {
                    while (waitUpdate)
                        Thread.Sleep(1);
                    return components;
                }
                else
                {
                    waitUpdate = true;
                    Send(new CommunicateBuffer(new RemoteComponent("RemoteContextComponentListGet"), null, null));
                    return OnUpdateComponents();
                }
            }

            protected virtual void CallReceived(CommunicateBuffer comp)
            {
                if(comp.Sender.Name == "RemoteContextComponentListPost")
                {
                    components = MessagePackSerializer.Deserialize<List<RemoteComponent>>(comp.Buffer);
                    foreach (var item in components)
                    {
                        item.Context = this;
                    }
                    waitUpdate = false;
                }
                else if(comp.Sender.Name == "RemoteContextComponentListGet")
                {
                    var compList = new List<RemoteComponent>();
                    foreach (var item in Communicator.Context.Components)
                    {
                        compList.Add(new RemoteComponent(item));
                    }
                    var buffer = MessagePackSerializer.Serialize(compList);
                    Send(new CommunicateBuffer(new RemoteComponent("RemoteContextComponentListPost"), null, buffer));
                }
                else
                {
                    Received?.Invoke(this, comp);
                }
            }
            public virtual Waiter Send(CommunicateBuffer buffer)
            {
                var waiter = new Waiter();
                BufferQueue.Enqueue((buffer, waiter));
                return waiter;
            }
            public abstract void Connect();
            public abstract void Disconnect();

            #region IDisposable Support
            private bool disposedValue = false;

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (IsConnected)
                        Disconnect();

                    disposedValue = true;
                }
            }

            ~RemoteContext()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }

        public class ClientRemoteContext : RemoteContext
        {
            UdpClient client;
            IPEndPoint localEp;
            Thread thread;
            Waiter connectWait;

            public override void Connect()
            {
                Logger.Log(this, "Connect");

                client = new UdpClient("127.0.0.1", 3377);
                localEp = new IPEndPoint(IPAddress.Any, 0);

                connectWait = new Waiter();

                thread = new Thread(new ParameterizedThreadStart(Proc));
                thread.IsBackground = true;
                thread.Name = "ClientRemoteContext";
                thread.Start();

                IsConnected = true;

                connectWait.Wait();
            }

            void Proc(object o)
            {
                Logger.Log(this, "Send handshake");
                var serialized = Encoding.UTF8.GetBytes("LatiumHS.1");
                client.Send(serialized, serialized.Length);

                Logger.Log(this, "Recieving handshake");
                var data = client.Receive(ref localEp);
                if (data.Length > 4096)
                {
                    Disconnect();
                    connectWait.Done();
                    return;
                }

                var decode = Encoding.UTF8.GetString(data);
                Logger.Log(this, "Recieved " + decode);
                if (decode != "OK")
                {
                    Logger.Error(this, "Unknown handshake "+decode);
                    Disconnect();
                    connectWait.Done();
                    return;
                }

                connectWait.Done();

                Task.Factory.StartNew(() =>
                {
                    while (IsConnected)
                    {
                        if (BufferQueue.TryDequeue(out (CommunicateBuffer, Waiter) frame))
                        {
                            serialized = frame.Item1.Serialize();

                            Logger.Log(this, $"Sending {serialized.Length}bytes");
                            client.Send(serialized, serialized.Length);

                            frame.Item2.Done();
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                });

                while (true)
                {
                    data = client.Receive(ref localEp);
                    Logger.Log(this, $"Recievend {data.Length}");

                    CallReceived(MessagePackSerializer.Deserialize<CommunicateBuffer>(data));
                }
            }

            public override void Disconnect()
            {
                client?.Close();
                client?.Dispose();
                client = null;

                IsConnected = false;
            }
        }

        public class Waiter
        {
            public bool IsSuccessed
            {
                get
                {
                    lock (locker) return isSuccessed;
                }
                set
                {
                    lock (locker) isSuccessed = value;
                }
            }
            public bool UnsafeIsSuccessed => isSuccessed;

            object locker = new object();
            bool isSuccessed = false;

            public void Done()
            {
                IsSuccessed = true;
            }

            public void Wait()
            {
                while (!IsSuccessed)
                    Thread.Sleep(1);
            }
        }

        public class ServerRemoteContext : RemoteContext
        {
            UdpClient client;
            IPEndPoint localEp;
            Thread thread;
            Waiter connectWait;

            public override void Connect()
            {
                Logger.Log(this, "Connect");

                IsConnected = true;

                client = new UdpClient();

                client.ExclusiveAddressUse = false;
                localEp = new IPEndPoint(IPAddress.Any, 3377);

                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.ExclusiveAddressUse = false;

                client.Client.Bind(localEp);
                connectWait = new Waiter();
                
                thread = new Thread(new ParameterizedThreadStart(Proc));
                thread.IsBackground = true;
                thread.Name = "ServerRemoteContext";
                thread.Start();

                connectWait.Wait();
            }

            void Proc(object o)
            {
                Logger.Log(this, "Wait for connection");

                var data = client.Receive(ref localEp);
                string response;
                if (data.Length > 4096)
                {
                    response = "Buffer overflow";
                }
                else
                {
                    var text = Encoding.UTF8.GetString(data);
                    Logger.Log(this, $"Recieved {text}");
                    if (text != "LatiumHS.V1")
                        response = "OK";
                    else
                        response = "Unknown Version";
                }

                Logger.Log(this, $"Send response {response}");
                data = Encoding.UTF8.GetBytes(response);
                client.Send(data, data.Length, localEp);

                if(response  != "OK")
                {
                    Disconnect();
                    connectWait.Done();
                    return;
                }

                connectWait.Done();

                Task.Factory.StartNew(() =>
                {
                    while (IsConnected)
                    {
                        if (BufferQueue.TryDequeue(out (CommunicateBuffer, Waiter) frame))
                        {
                            var serialized = frame.Item1.Serialize();

                            Logger.Log(this, $"Sending {serialized.Length} bytes");
                            client.Send(serialized, serialized.Length, localEp);

                            frame.Item2.Done();
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                });

                while (true)
                {
                    data = client.Receive(ref localEp);
                    Logger.Log("Recieved");
                    CallReceived(MessagePackSerializer.Deserialize<CommunicateBuffer>(data));
                }
            }

            public override void Disconnect()
            {
                client?.Close();
                client?.Dispose();
                client = null;

                IsConnected = false;
            }
        }

        public class RemoteComponentComparer : IEqualityComparer<RemoteComponent>
        {
            public bool Equals(RemoteComponent x, RemoteComponent y)
            {
                return x.In(y);
            }

            public int GetHashCode(RemoteComponent obj)
            {
                return obj.GetHashCode();
            }
        }

        public class Communicator : Component
        {
            public IEnumerable<RemoteContext> Remotes => remotes;

            protected Dictionary<Component, Action<CommunicateBuffer>> Callbacks = new Dictionary<Component, Action<CommunicateBuffer>>();
            List<RemoteContext> remotes = new List<RemoteContext>();

            ConcurrentQueue<CommunicateBuffer> bufferQueue = new ConcurrentQueue<CommunicateBuffer>();
            Thread thread;

            public Communicator()
            {
                thread = new Thread(new ParameterizedThreadStart((o) =>
                {
                    while (true)
                    {
                        if (bufferQueue.TryDequeue(out CommunicateBuffer buffer))
                        {
                            foreach (var remote in remotes)
                            {
                                if (remote.Components.Intersect(buffer.Targets, new RemoteComponentComparer()).Any())
                                    remote.Send(buffer);
                            }
                        }
                        if (bufferQueue.Count == 0)
                            Thread.Sleep(1);
                    }
                }));
                thread.IsBackground = true;
                thread.Name = "Communicator Pool";
            }

            public List<RemoteComponent> GetComponents(RemoteComponent component)
            {
                var ret = new List<RemoteComponent>();
                foreach (var remote in remotes)
                {
                    var q = remote.GetComponents(component);
                    ret.AddRange(q);
                }
                return ret;
            }

            public void AddRemote(RemoteContext c)
            {
                c.Communicator = this;
                c.Received += OnRecive;
                remotes.Add(c);
            }

            private void OnRecive(object sender, CommunicateBuffer e)
            {
                RunCallback(e);
            }

            public void RemoveRemote(RemoteContext c)
            {
                c.Communicator = null;
                c.Received -= OnRecive;
                remotes.Remove(c);
            }

            public void Enqueue(Component comp, List<RemoteComponent> targets, byte[] buffer)
            {
                Enqueue(new CommunicateBuffer(comp, targets, buffer));
            }

            public void Enqueue(CommunicateBuffer buffer)
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
                var q = Callbacks.Keys.Where((c)=>{
                    var data = new RemoteComponent(c);
                    return buffer.Targets.Where((t) => data.In(t)).Any();
                });

                if (q.Any())
                {
                    foreach (var item in q)
                    {
                        Callbacks[item].Invoke(buffer);
                    }
                }
            }

            public override void OnLoad()
            {
                base.OnLoad();

                foreach (var remote in remotes)
                    remote.Connect();

                thread.Start();
            }

            public override void OnShutdown()
            {
                base.OnShutdown();

                foreach (var remote in remotes)
                {
                    remote.Disconnect();
                }

                thread?.Abort();
                thread = null;
            }
        }
    }
}