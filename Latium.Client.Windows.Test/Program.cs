using System;
using Latium.Net;
using Latium.IO.Camera;
using Latium;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace Latium.Client.Windows.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new Context();
            server.Communicator.AddRemote(new ServerRemoteContext());
            var remoteCam = new CameraRemote();
            remoteCam.Captured += (sender, frame) => { Cv2.ImShow("server", frame); Cv2.WaitKey(1); };
            server.AddComponent(remoteCam);
            server.Init();
            server.Load();

            var client = new Context();
            client.Communicator.AddRemote(new ClientRemoteContext());
            client.AddComponent(new OcvCamera(0));
            client.Init();
            client.Load();

            while (true) Thread.Sleep(10);
        }
    }
}
