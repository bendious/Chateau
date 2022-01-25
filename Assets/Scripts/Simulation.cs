using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// The Simulation class implements the discrete event simulator pattern.
/// Events are pooled, with a default capacity of 4 instances.
/// </summary>
public static partial class Simulation
{
	static readonly HeapQueue<Event> eventQueue = new();
	static readonly Dictionary<System.Type, Stack<Event>> eventPools = new();


	/// <summary>
	/// Create a new event of type T and return it, but do not schedule it.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	static public T New<T>() where T : Event, new()
	{
		if (!eventPools.TryGetValue(typeof(T), out Stack<Event> pool))
		{
			pool = new(4);
			pool.Push(new T());
			eventPools[typeof(T)] = pool;
		}
		if (pool.Count > 0)
		{
			return (T)pool.Pop();
		}
		else
		{
			return new();
		}
	}

	/// <summary>
	/// Clear all pending events and reset the tick to 0.
	/// </summary>
	public static void Clear()
	{
		eventQueue.Clear();
	}

	/// <summary>
	/// Schedule an event for a future tick, and return it.
	/// </summary>
	/// <returns>The event.</returns>
	/// <param name="tick">Tick.</param>
	/// <typeparam name="T">The event type parameter.</typeparam>
	static public T Schedule<T>(float tick = 0) where T : Event, new()
	{
		T ev = New<T>();
		ev.tick = Time.time + tick;
		eventQueue.Push(ev);
		return ev;
	}

	/// <summary>
	/// Reschedule an existing event for a future tick, and return it.
	/// </summary>
	/// <returns>The event.</returns>
	/// <param name="tick">Tick.</param>
	/// <typeparam name="T">The event type parameter.</typeparam>
	static public T Reschedule<T>(T ev, float tick) where T : Event, new()
	{
		ev.tick = Time.time + tick;
		eventQueue.Push(ev);
		return ev;
	}

	/// <summary>
	/// Tick the simulation. Returns the count of remaining events.
	/// If remaining events is zero, the simulation is finished unless events are
	/// injected from an external system via a Schedule() call.
	/// </summary>
	/// <returns></returns>
	static public int Tick()
	{
		float time = Time.time;
		int executedEventCount = 0;
		while (eventQueue.Count > 0 && eventQueue.Peek().tick <= time)
		{
			Event ev = eventQueue.Pop();
			float tick = ev.tick;
			ev.ExecuteEvent();
			if (ev.tick > tick)
			{
				//event was rescheduled, so do not return it to the pool.
			}
			else
			{
				// Debug.Log($"<color=green>{ev.tick} {ev.GetType().Name}</color>");
				ev.Cleanup();
				try
				{
					eventPools[ev.GetType()].Push(ev);
				}
				catch (KeyNotFoundException)
				{
					//This really should never happen inside a production build.
					Debug.LogError($"No Pool for: {ev.GetType()}");
				}
			}
			executedEventCount++;
		}
		return eventQueue.Count;
	}
}
