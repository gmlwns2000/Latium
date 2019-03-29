using Latium.IO.Camera;
using Latium.Net;
using MessagePack;
using OpenCvSharp;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Latium.Client.Windows.Test
{
    class Adder : Component
    {
        List<RemoteComponent> targets;

        public override void OnInit()
        {
            base.OnInit();
            Name = "Adder";
        }

        public override void OnLoad()
        {
            base.OnLoad();
            targets = Communicator.GetComponents(new RemoteComponent("Adder"));
            Communicator.Enqueue(new CommunicateBuffer(this, targets, MessagePackSerializer.Serialize(0L)));
        }

        public override void OnRecive(CommunicateBuffer buffer)
        {
            base.OnRecive(buffer);
            long d = MessagePackSerializer.Deserialize<long>(buffer.Buffer) + 3;
            Logger.Log(ToString(), d.ToString());
            Communicator.Enqueue(this, targets, MessagePackSerializer.Serialize(d));
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            Task.Factory.StartNew(() =>
            {
                var server = new Context();
                server.Communicator.AddRemote(new ServerRemoteContext());
                var remoteCam = new CameraRemote();
                remoteCam.Captured += (sender, frame) => { Cv2.ImShow("server", frame); Cv2.WaitKey(1); };
                server.AddComponent(remoteCam);
                server.AddComponent(new Adder());
                server.Init();
                server.Load();
            });

            Thread.Sleep(1000);

            var client = new Context();
            client.Communicator.AddRemote(new ClientRemoteContext());
            client.AddComponent(new OcvCamera(0));
            client.AddComponent(new Adder());
            client.Init();
            client.Load();

            while (true) Thread.Sleep(10);
        }
    }
}
