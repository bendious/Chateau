using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.VFX;


[Serializable]
public sealed class WeightedObject<T>
{
	public T m_object;
	public float m_weight = 1.0f;
}

// used to return values from SendMessage() calls
public sealed class SendMessageValue<OutT>
{
	public OutT m_out;
}
public sealed class SendMessageValue<InT, OutT>
{
	public InT m_in;
	public OutT m_out;
}


public static class Utility
{
	public const float FloatEpsilon = 0.01f;


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

	public static int EnumNumTypes<T>() => Enum.GetValues(typeof(T)).Length;

	public static bool BitsSet<T>(this T x, T bits)
	{
		int bitsInt = (int)(object)bits;
		return ((int)(object)x & bitsInt) == bitsInt;
	}

	public static Vector2 MinMax<TIn>(this IEnumerable<TIn> v, Func<TIn, float> selector) => v == null || v.Count() <= 0 ? default : new(v.Min(selector), v.Max(selector)); // TODO: efficiency? better default?

	public static IEnumerable<WeightedObject<T>> CombineWeighted<T>(this IEnumerable<WeightedObject<T>> a, IEnumerable<WeightedObject<T>> b) => CombineWeighted(a, b, WeightedObjectToObject, WeightedObjectToObject);

	public static IEnumerable<WeightedObject<T1>> CombineWeighted<T1, T2, TKey>(this IEnumerable<WeightedObject<T1>> a, IEnumerable<WeightedObject<T2>> b, Func<WeightedObject<T1>, TKey> aToKey, Func<WeightedObject<T2>, TKey> bToKey) => a.Join(b, aToKey, bToKey, (pair1, pair2) => new WeightedObject<T1> { m_object = pair1.m_object, m_weight = pair1.m_weight * pair2.m_weight });

	public static T RandomWeighted<T>(this IEnumerable<WeightedObject<T>> pairs)
	{
		return RandomWeighted(pairs.Select(WeightedObjectToObject), pairs.Select(pair => pair.m_weight));
	}

	public static T RandomWeighted<T>(this IEnumerable<T> values, IEnumerable<float> weights)
	{
		Assert.IsFalse(weights.Any(f => f < 0.0f));

		// NOTE the array slice to handle values[] w/ shorter length than weights[] by ignoring the excess weights; the opposite situation works out equivalently w/o explicit handling since weightRandom will never result in looping beyond the number of weights given
		int valueCount = values.Count();
		Debug.Assert(valueCount == weights.Count());
		float weightSum = weights.Sum();
		Debug.Assert(weightSum > 0.0f);
		float weightRandom = UnityEngine.Random.Range(0.0f, weightSum);

		int idxItr = 0;
		while (weightRandom > weights.ElementAt(idxItr))
		{
			weightRandom -= weights.ElementAt(idxItr);
			++idxItr;
		}

		Assert.IsTrue(weightRandom >= 0.0f && idxItr < valueCount);
		return values.ElementAt(idxItr);
	}

	public static IEnumerable<T> RandomWeightedOrder<T>(this IEnumerable<WeightedObject<T>> pairs)
	{
		Assert.IsFalse(pairs.Any(pair => pair.m_weight < 0.0f));
		return pairs.OrderBy(pair => UnityEngine.Random.value / pair.m_weight).Select(WeightedObjectToObject);
	}

	public static IEnumerable<T> RandomWeightedOrder<T>(this IEnumerable<T> values, IEnumerable<float> weights) => RandomWeightedOrder(values.Zip(weights, (value, weight) => new WeightedObject<T> { m_object = value, m_weight = weight }));

	public static T RandomWeightedEnum<T>(this float[] weights) where T : Enum
	{
		/*const*/ int typeCount = EnumNumTypes<T>();
		Assert.IsTrue(weights.Length <= typeCount);
		return RandomWeighted(Enumerable.Range(0, typeCount).Select(i => {
			Assert.IsTrue(Enum.IsDefined(typeof(T), i));
			return (T)Enum.ToObject(typeof(T), i);
		}).ToArray(), weights);
	}

	// NOTE that Mathf.Approximately() uses float.Epsilon, which is uselessly strict
	public static bool FloatEqual(this float a, float b, float epsilon = FloatEpsilon) => Mathf.Abs(a - b) < epsilon;

	public static bool FloatEqualDegrees(this float a, float b, float epsilon)
	{
		float aMod = a.Modulo(360.0f);
		float bMod = b.Modulo(360.0f);
		return aMod.FloatEqual(bMod, epsilon) || !aMod.FloatEqual(bMod, 360.0f - epsilon);
	}

	public static bool ColorsSimilar(this Color a, Color b, float epsilon = 0.2f)
	{
		return FloatEqual(a.r, b.r, epsilon) && FloatEqual(a.g, b.g, epsilon) && FloatEqual(a.b, b.b, epsilon); // NOTE that we don't use color subtraction due to not wanting range clamping
	}

	public static Color ColorRandom(Color min, Color max, bool proportional, float epsilon = 0.2f)
	{
		float[] pcts = proportional ? Enumerable.Repeat(UnityEngine.Random.value, 4).ToArray() : new[] { UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value };
		Color color = new(Mathf.Lerp(min.r, max.r, pcts[0]), Mathf.Lerp(min.g, max.g, pcts[1]), Mathf.Lerp(min.b, max.b, pcts[2]), Mathf.Lerp(min.a, max.a, pcts[3]));
		if (ColorsSimilar(color, RoomController.m_oneWayPlatformColor, epsilon) || ColorsSimilar(color, Color.black, epsilon))
		{
			if (proportional)
			{
				for (int i = 0; i < 3; ++i)
				{
					color = color.ColorFlipComponent(i, min, max);
				}
			}
			else
			{
				color = color.ColorFlipComponent(UnityEngine.Random.Range(0, 3), min, max);
			}
		}
		return color;
	}

	public static Color ColorFlipComponent(this Color color, int idx, Color min, Color max)
	{
		// flip one component as far as it can go within the given range
		float minComp = min[idx];
		float maxComp = max[idx];
		color[idx] = Mathf.Lerp(minComp, maxComp, (Mathf.InverseLerp(minComp, maxComp, color[idx]) + 0.5f) % 1.0f);
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

	public static float ManhattanDistance(this Vector2 v1, Vector2 v2) => Mathf.Abs(v2.x - v1.x) + Mathf.Abs(v2.y - v1.y);

	public static Vector2 Abs(this Vector2 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y));

	public static Vector2 Clamp(this Vector2 v, float min, float max) => new(Mathf.Clamp(v.x, min, max), Mathf.Clamp(v.y, min, max));

	public static Vector2 SmoothDamp(this Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime) => SmoothDamp(current, target, ref currentVelocity, new Vector2(smoothTime, smoothTime));

	public static Vector2 SmoothDamp(this Vector2 current, Vector2 target, ref Vector2 currentVelocity, Vector2 smoothTimes)
	{
		current.x = Mathf.SmoothDamp(current.x, target.x, ref currentVelocity.x, smoothTimes.x);
		current.y = Mathf.SmoothDamp(current.y, target.y, ref currentVelocity.y, smoothTimes.y);
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

	public static Vector2 OriginToCenterY(this GameObject obj, bool useRenderers = false)
	{
		Component[] components = useRenderers ? obj.GetComponentsInChildren<Renderer>() : obj.GetComponentsInChildren<Collider2D>();
		float yMin = components.Length <= 0 ? 0.0f : useRenderers ? components.Min(component => (component as Renderer).bounds.min.y) : components.Min(component =>
		{
			Collider2D collider = component as Collider2D;
			float colliderExtentY = collider is CapsuleCollider2D capsule ? capsule.size.y * 0.5f : collider is CircleCollider2D circle ? circle.radius : collider is BoxCollider2D box ? box.size.y * 0.5f + box.edgeRadius : 0.0f; // NOTE that we can't use Collider2D.bounds on an uninstantiated prefab // TODO: less hardcoding?
			return collider.transform.position.y - obj.transform.position.y + collider.offset.y - colliderExtentY;
		});
		return new(0.0f, -yMin);
	}

	public static int ToIndex(this LayerMask mask)
	{
		Debug.Assert(mask > 0 && Mathf.IsPowerOfTwo(mask)); // multi-bit masks don't translate to a single index
		return Mathf.RoundToInt(Mathf.Log(mask, 2.0f));
	}

	// see String.Replace() as well as https://lonewolfonline.net/replace-first-occurrence-string/
	public static string ReplaceFirst(this string source, string oldValue, string newValue)
	{
		int index = source.IndexOf(oldValue);
		if (index < 0)
		{
			return source;
		}
		return source.Remove(index, oldValue.Length).Insert(index, newValue);
	}

	public static System.Collections.IEnumerator SoftStop(this VisualEffect vfx, Func<bool> shouldCancelFunc = null, float delayMax = 2.0f, bool wholeObject = true)
	{
		vfx.Stop();

		// TODO: fade out lights while waiting?
		float waitTimeMax = Time.time + delayMax; // NOTE that if offscreen, the particles will stop simulating and never die until visible again, in which case we want to disable the object and not worry about killing particles

		yield return new WaitUntil(() => vfx.aliveParticleCount <= 0 || Time.time > waitTimeMax || (shouldCancelFunc != null && shouldCancelFunc()));

		if (shouldCancelFunc != null && shouldCancelFunc())
		{
			yield break;
		}

		if (wholeObject)
		{
			vfx.gameObject.SetActive(false);
		}
		else
		{
			vfx.enabled = false;
		}
	}

	public static void NonpublicSetterWorkaround(this object component, string fieldName, object value)
	{
		// see https://forum.unity.com/threads/lwrp-light-2d-change-sprite-in-script.753542/ for explanation of workaround for serialized properties not having public setters
		const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
		System.Reflection.FieldInfo setterWorkaround = component.GetType().GetField(fieldName, flags);
		setterWorkaround.SetValue(component, value);
	}


	private static T WeightedObjectToObject<T>(WeightedObject<T> pair) => pair.m_object;
}
