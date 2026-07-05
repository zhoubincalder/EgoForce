using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;


public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<System.Action> _executionQueue = new Queue<System.Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            GameObject obj = new GameObject("UnityMainThreadDispatcher");
            _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(obj);
        }
        return _instance;
    }
    private static UnityMainThreadDispatcher _instance;
    
    private void Update()
    {
        if (_executionQueue == null)
        {
            Debug.LogError("Execution queue is null.");
            return;
        }

        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                var action = _executionQueue.Dequeue();
                action?.Invoke(); // Safe invocation
            }
        }
    }
    public Task Enqueue(System.Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        _executionQueue.Enqueue(() =>
        {
            try
            {
                action.Invoke();
                tcs.SetResult(true);
            }
            catch (System.Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
    public Task<T> Enqueue<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        _executionQueue.Enqueue(() =>
        {
            try
            {
                T result = action();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

}
