using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


[Serializable]
public class WeightedObject<T>
{
	public T m_object;
	public float m_weight = 1.0f;
}


public static class Utility
{
	public static int Modulo(this int x, int m)
	{
		int r = x % m;
		return (r < 0) ? r + m : r;
	}

	public static float Modulo(this float x, float m)
	{
		float r = x % m;
		return (r < 0) ? r + m : r;
	}

	public static float Fract(this float x) => x - (float)Math.Truncate(x);

	public static int EnumNumTypes<T>()
	{
		return Enum.GetValues(typeof(T)).Length;
	}

	public static T RandomWeighted<T>(this System.Collections.Generic.IEnumerable<WeightedObject<T>> pairs)
	{
		return RandomWeighted(pairs.Select(pair => pair.m_object).ToArray(), pairs.Select(pair => pair.m_weight).ToArray());
	}

	public static T RandomWeighted<T>(this T[] values, float[] weights)
	{
		Assert.IsFalse(weights.Any(f => f < 0.0f));

		// NOTE the array slice to handle values[] w/ shorter length than weights[] by ignoring the excess weights; the opposite situation works out equivalently w/o explicit handling since weightRandom will never result in looping beyond the number of weights given
		float weightSum = weights[0 .. Math.Min(values.Length, weights.Length)].Sum();
		float weightRandom = UnityEngine.Random.Range(0.0f, weightSum);

		int idxItr = 0;
		while (weightRandom >= weights[idxItr])
		{
			weightRandom -= weights[idxItr];
			++idxItr;
		}

		Assert.IsTrue(weightRandom >= 0.0f && idxItr < values.Length);
		return values[idxItr];
	}

	public static T[] RandomWeightedOrder<T>(this WeightedObject<T>[] pairs)
	{
		Assert.IsFalse(pairs.Any(pair => pair.m_weight < 0.0f));
		return pairs.OrderBy(pair => UnityEngine.Random.value / pair.m_weight).Select(pair => pair.m_object).ToArray();
	}

	public static T[] RandomWeightedOrder<T>(this T[] values, float[] weights)
	{
		return RandomWeightedOrder(values.Zip(weights, (value, weight) => new WeightedObject<T> { m_object = value, m_weight = weight }).ToArray());
	}

	public static T RandomWeightedEnum<T>(this float[] weights) where T : System.Enum
	{
		/*const*/ int typeCount = EnumNumTypes<T>();
		Assert.IsTrue(weights.Length <= typeCount);
		return RandomWeighted(Enumerable.Range(0, typeCount).Select(i => {
			Assert.IsTrue(Enum.IsDefined(typeof(T), i));
			return (T)Enum.ToObject(typeof(T), i);
		}).ToArray(), weights);
	}

	// NOTE that Mathf.Approximately() uses float.Epsilon, which is uselessly strict
	public static bool FloatEqual(this float a, float b, float epsilon = 0.01f)
	{
		return Mathf.Abs(a - b) < epsilon;
	}

	public static bool ColorsSimilar(this Color a, Color b, float epsilon = 0.2f)
	{
		return FloatEqual(a.r, b.r, epsilon) && FloatEqual(a.g, b.g, epsilon) && FloatEqual(a.b, b.b, epsilon); // NOTE that we don't use color subtraction due to not wanting range clamping
	}

	public static Color ColorRandom(float min, float max, float epsilon = 0.2f)
	{
		Color color = new(UnityEngine.Random.Range(min, max), UnityEngine.Random.Range(min, max), UnityEngine.Random.Range(min, max));
		if (ColorsSimilar(color, RoomController.m_oneWayPlatformColor, epsilon) || ColorsSimilar(color, Color.black, epsilon))
		{
			// flip one component as far as it can go within the given range
			int idx = UnityEngine.Random.Range(0, 3);
			color[idx] = Mathf.Lerp(min, max, (Mathf.InverseLerp(min, max, color[idx]) + 0.5f) % 1.0f);
		}
		return color;
	}

	public static Vector4 Pow(this Vector4 v, float p)
	{
		v.x = Mathf.Pow(v.x, p);
		v.y = Mathf.Pow(v.y, p);
		v.z = Mathf.Pow(v.z, p);
		v.w = Mathf.Pow(v.w, p);
		return v;
	}

	public static Vector2 Abs(this Vector2 v)
	{
		return new Vector2(Mathf.Abs(v.x), Mathf.Abs(v.y));
	}

	public static Vector2 Clamp(this Vector2 v, float min, float max)
	{
		return new Vector2(Mathf.Clamp(v.x, min, max), Mathf.Clamp(v.y, min, max));
	}

	public static Vector2 SmoothDamp(this Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime)
	{
		current.x = Mathf.SmoothDamp(current.x, target.x, ref currentVelocity.x, smoothTime);
		current.y = Mathf.SmoothDamp(current.y, target.y, ref currentVelocity.y, smoothTime);
		return current;
	}

	public static Color SmoothDamp(this Color current, Color target, ref Vector4 currentVelocity, float smoothTime)
	{
		current.r = Mathf.SmoothDamp(current.r, target.r, ref currentVelocity.x, smoothTime);
		current.g = Mathf.SmoothDamp(current.g, target.g, ref currentVelocity.y, smoothTime);
		current.b = Mathf.SmoothDamp(current.b, target.b, ref currentVelocity.z, smoothTime);
		current.a = Mathf.SmoothDamp(current.a, target.a, ref currentVelocity.w, smoothTime);
		return current;
	}
}
