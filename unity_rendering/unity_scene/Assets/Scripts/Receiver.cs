using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using UnityEngine;

public class Receiver : StopableThread
{
    public readonly ConcurrentQueue<byte[]> toEventLoop;

    public Receiver()
    {
        toEventLoop = new ConcurrentQueue<byte[]>();
        ForceDotNet.Force();
    }

    protected override void Run()
    {
        using var socket = new PullSocket();
        socket.Connect("tcp://localhost:5555");
        while (Running) {
            if (socket.TryReceiveFrameBytes(out byte[] data))
            {                                
                toEventLoop.Enqueue(data);
            }
        }
    }
}