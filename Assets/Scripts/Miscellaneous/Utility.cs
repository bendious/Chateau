using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;


[Serializable]
public sealed class WeightedObject<T>
{
	public T m_object;
	public float m_weight = 1.0f;
}

public sealed class PriorityDistanceComparer : IComparer<Tuple<float, float>>
{
	public int Compare(Tuple<float, float> x, Tuple<float, float> y)
	{
		int c = y.Item1.CompareTo(x.Item1); // NOTE the x/y reversal since HIGHER priorities should be sorted earlier in the output
		return c != 0 ? c : x.Item2.CompareTo(y.Item2);
	}
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

	public static T1 SelectMin<T1, T2>(this IEnumerable<T1> options, Func<T1, T2> valueFunc, IComparer<T2> comparer = null) => options.OrderBy(valueFunc, comparer).FirstOrDefault();

	public static T1 SelectMax<T1, T2>(this IEnumerable<T1> options, Func<T1, T2> valueFunc, IComparer<T2> comparer = null) => options.OrderBy(valueFunc, comparer).LastOrDefault();

	public static Tuple<T1, T2> SelectMinWithValue<T1, T2>(this IEnumerable<T1> options, Func<T1, T2> valueFunc, IComparer<T2> comparer = null) => options.Select(option => Tuple.Create(option, valueFunc(option))).OrderBy(pair => pair.Item2, comparer).First();

	public static IEnumerable<WeightedObject<T>> CombineWeighted<T>(this IEnumerable<WeightedObject<T>> a, IEnumerable<WeightedObject<T>> b) => CombineWeighted(a, b, WeightedObjectToObject, WeightedObjectToObject);

	public static IEnumerable<WeightedObject<T1>> CombineWeighted<T1, T2, TKey>(this IEnumerable<WeightedObject<T1>> a, IEnumerable<WeightedObject<T2>> b, Func<WeightedObject<T1>, TKey> aToKey, Func<WeightedObject<T2>, TKey> bToKey) => a.Join(b, aToKey, bToKey, (pair1, pair2) => new WeightedObject<T1> { m_object = pair1.m_object, m_weight = pair1.m_weight * pair2.m_weight });

	public static T Random<T>(this IEnumerable<T> options) => options == null || options.Count() <= 0 ? default : options.ElementAt(UnityEngine.Random.Range(0, options.Count()));

	public static T RandomWeighted<T>(this IEnumerable<WeightedObject<T>> pairs) => pairs.ElementAt(pairs.RandomWeightedIndex()).m_object;

	public static T RandomWeighted<T>(this IEnumerable<T> values, IEnumerable<float> weights) => values.ElementAt(values.RandomWeightedIndex(weights));

	public static int RandomWeightedIndex<T>(this IEnumerable<WeightedObject<T>> pairs) => RandomWeightedIndex(pairs.Select(WeightedObjectToObject), pairs.Select(pair => pair.m_weight));

	public static int RandomWeightedIndex<T>(this IEnumerable<T> values, IEnumerable<float> weights)
	{
		float[] weightsArray = weights.ToArray(); // to avoid evaluating weights multiple times if given LINQ expressions
		Assert.IsFalse(weightsArray.Any(f => f < 0.0f));

		int valueCount = values.Count();
		Debug.Assert(valueCount == weightsArray.Length); // NOTE that we could use an array slice to handle values[] w/ shorter length than weights[] by ignoring the excess weights (the opposite situation works out equivalently w/o explicit handling since weightRandom will never result in looping beyond the number of weights given), but as of yet that hasn't been necessary
		float weightSum = weightsArray.Sum();
		Debug.Assert(weightSum > 0.0f);
		float weightRandom = UnityEngine.Random.Range(float.Epsilon, weightSum);

		int idxItr = 0;
		while (weightRandom > weightsArray[idxItr])
		{
			weightRandom -= weightsArray[idxItr];
			++idxItr;
		}

		Assert.IsTrue(weightRandom >= 0.0f && idxItr < valueCount);
		return idxItr;
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

	public static float ZRadians(Vector2 v) => Mathf.Atan2(v.y, v.x);

	public static float ZDegrees(Vector2 v) => Mathf.Rad2Deg * ZRadians(v);

	public static Quaternion ZRotation(Vector2 v) => Quaternion.Euler(0.0f, 0.0f, ZDegrees(v));

	public static Bounds BoundsRotated(Bounds b, Quaternion q, bool local)
	{
		// handle rotation by reseting and expanding to rotated corners
		// TODO: efficiency? allow rotation around arbitrary points?
		Vector3 centerOrig = local ? b.center : q * b.center;
		Vector3 extentsOrig = b.extents;

		b.center = centerOrig;
		b.extents = Vector3.zero;

		for (int i = -1; i < 2; i += 2)
		{
			for (int j = -1; j < 2; j += 2)
			{
				b.Encapsulate(centerOrig + q * Vector3.Scale(extentsOrig, new(i, j, 1.0f)));
			}
		}

		return b;
	}

	public static bool ColorsSimilar(this Color a, Color b, float epsilon = 0.2f)
	{
		return FloatEqual(a.r, b.r, epsilon) && FloatEqual(a.g, b.g, epsilon) && FloatEqual(a.b, b.b, epsilon); // NOTE that we don't use color subtraction due to not wanting range clamping
	}

	public static Color ColorRandom(Color min, Color max, bool proportional, float epsilon = 0.2f, params Color[] colorsToAvoid)
	{
		float[] pcts = proportional ? Enumerable.Repeat(UnityEngine.Random.value, 4).ToArray() : new[] { UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value };
		Color color = new(Mathf.Lerp(min.r, max.r, pcts[0]), Mathf.Lerp(min.g, max.g, pcts[1]), Mathf.Lerp(min.b, max.b, pcts[2]), Mathf.Lerp(min.a, max.a, pcts[3]));

		if (ColorsSimilar(color, RoomController.m_oneWayPlatformColor, epsilon) || ColorsSimilar(color, Color.black, epsilon) || colorsToAvoid.Any(colorToAvoid => color.ColorsSimilar(colorToAvoid, epsilon)))
		{
			// TODO: ensure new color does not happen to be similar to any colors to avoid?
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

	public static float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, float mass, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float dampingFactor = 2.0f * Mathf.Sqrt(stiffness * mass) * dampPct;
		Debug.Assert(dampingFactor > 0.0f);
		float diff = target - current;
		while (isAngle && Mathf.Abs(diff) > 180.0f)
		{
			diff -= diff < 0.0f ? -360.0f : 360.0f;
		}
		float force = stiffness * diff - dampingFactor * velocityCurrent;

		float accel = force / mass;
		float dt = Time.fixedDeltaTime;
		velocityCurrent += accel * dt;

		return current + velocityCurrent * dt;
	}

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

	public static Health ToHealth(this Collider2D collider)
	{
		if (collider == null)
		{
			return null;
		}
		Health health = collider.GetComponent<Health>();
		if (health != null || collider.attachedRigidbody == null || collider.attachedRigidbody.gameObject == collider.gameObject)
		{
			return health;
		}
		return collider.attachedRigidbody.GetComponent<Health>();
	}

	public static Bounds ToBounds(this IEnumerable<Collider2D> colliders) => colliders.Aggregate(new Bounds() { size = Vector3.negativeInfinity }, (bounds, collider) => { bounds.Encapsulate(collider.bounds); return bounds; });

	public static bool ShouldIgnore(this Collider2D self, Rigidbody2D body, Collider2D[] colliders, float dynamicsMassThreshold = 0.0f, Type ignoreChildrenExcept = null, float oneWayTopEpsilon = -1.0f, bool ignoreStatics = false, bool ignorePhysicsSystem = false)
	{
		Assert.IsTrue(colliders != null && colliders.Length > 0);
		GameObject otherObj = colliders.First(collider => collider != null).gameObject; // NOTE that we don't use the rigid body's object since that can be separate from the collider object (e.g. characters and arms) // TODO: ensure all colliders are from the same object & body?
		if (otherObj == self.gameObject)
		{
			return true; // ignore our own object
		}
		if (ignoreStatics && (body == null || body.bodyType == RigidbodyType2D.Static))
		{
			return true;
		}
		if (body != null && body.bodyType == RigidbodyType2D.Dynamic && body.mass < dynamicsMassThreshold)
		{
			return true;
		}
		if (ignoreChildrenExcept != null && body != null && (body.transform.parent != null || body.gameObject != otherObj) && body.GetComponent(ignoreChildrenExcept) == null)
		{
			return true; // ignore non-root bodies (e.g. arms)
		}
		if (otherObj.GetComponentsInParent<Transform>().Intersect(self.GetComponentsInParent<Transform>()).Count() > 0) // TODO: too broad? efficiency?
		{
			return true; // ignore child/sibling objects
		}

		// if partway through a one-way platform, ignore it
		if (oneWayTopEpsilon >= 0.0f && colliders.All(collider => collider == null || (collider.gameObject.layer == GameController.Instance.m_layerOneWay.ToIndex() && (oneWayTopEpsilon == float.MaxValue || (self.bounds.center != Vector3.zero && self.bounds.min.y + oneWayTopEpsilon < collider.bounds.max.y))))) // TODO: better way of skipping self.bounds check on first frame before validity?
		{
			if (body != null && body.bodyType != RigidbodyType2D.Static)
			{
				EnableCollision.TemporarilyDisableCollision(new[] { self }, colliders, 0.25f); // NOTE that this is necessary to prevent "breaking" rope ladders due to constant collision // TODO: parameterize ignore time?
			}
			return true;
		}

		if (ignorePhysicsSystem)
		{
			return false;
		}

		// ignore objects flagged to ignore each other and their children
		// TODO: efficiency?
		foreach (Collider2D collider in colliders)
		{
			if (Physics2D.GetIgnoreCollision(self, collider))
			{
				return true;
			}
			if (self.transform.parent != null)
			{
				Collider2D parentCollider = self.transform.parent.GetComponent<Collider2D>();
				if (parentCollider != null && Physics2D.GetIgnoreCollision(parentCollider, collider))
				{
					return true;
				}
			}
		}
		if (otherObj.transform.parent != null)
		{
			Collider2D parentCollider = otherObj.transform.parent.GetComponent<Collider2D>();
			if (parentCollider != null && Physics2D.GetIgnoreCollision(self, parentCollider))
			{
				return true;
			}
		}

		return false;
	}

	public enum SoftStopPost
	{
		DeactivateRoot,
		DeactivateChildren,
		DisableComponents,
		Reactivate,
	}
	public static System.Collections.IEnumerator SoftStop(this GameObject rootObj, Func<bool> shouldCancelFunc = null, float delayMax = 2.0f, SoftStopPost postBehavior = SoftStopPost.DeactivateRoot)
	{
		VisualEffect[] vfxAll = rootObj.GetComponentsInChildren<VisualEffect>();
		foreach (VisualEffect vfx in vfxAll)
		{
			vfx.Stop();
		}

		// gather lights to fade out
		// TODO: fade out other effects, too?
		Tuple<LightFlickerSynced, Light2D, float>[] lights = rootObj.GetComponentsInChildren<Light2D>().Select(light => Tuple.Create(light.GetComponent<LightFlickerSynced>(), light, light.intensity)).ToArray(); // TODO: ensure none of the intensities are temporary due to effects other than flickering?

		// determine wait params
		float waitTimeMax = Time.time + delayMax; // NOTE that if offscreen, the particles will stop simulating and never die until visible again, in which case we want to disable the object and not worry about killing particles
		bool waitFunc() => vfxAll.Max(vfx => vfx.aliveParticleCount) <= 0 || Time.time > waitTimeMax || (shouldCancelFunc != null && shouldCancelFunc());

		// wait
		if (lights.Length <= 0)
		{
			yield return new WaitUntil(waitFunc);
		}
		else
		{
			do
			{
				yield return null; // NOTE that due to how GameController.IndicateUpgrade()/HealthUpgrade() are ordered, we have to be sure to wait before checking waitFunc() in order to let UpgradeActiveCount be incremented/decremented first

				// fade lights while waiting
				float scalar = (waitTimeMax - Time.time) / delayMax; // TODO: don't rely on delayMax being about how long the particles take to stop?
				foreach (Tuple<LightFlickerSynced, Light2D, float> light in lights)
				{
					if (light.Item1 != null)
					{
						light.Item1.IntensityScalar = scalar;
					}
					else
					{
						light.Item2.intensity = light.Item3 * scalar;
					}
				}
			}
			while (!waitFunc());
		}

		// reset light intensities for when re-enabled
		foreach (Tuple<LightFlickerSynced, Light2D, float> light in lights)
		{
			if (light.Item1 != null)
			{
				light.Item1.IntensityScalar = 1.0f;
			}
			else
			{
				light.Item2.intensity = light.Item3;
			}
		}

		if (postBehavior == SoftStopPost.Reactivate || (shouldCancelFunc != null && shouldCancelFunc()))
		{
			foreach (VisualEffect vfx in vfxAll)
			{
				vfx.Play();
			}
			yield break;
		}

		if (postBehavior == SoftStopPost.DeactivateRoot)
		{
			rootObj.SetActive(false);
		}
		else
		{
			Action<Behaviour> disableOrDeactivate = postBehavior == SoftStopPost.DeactivateChildren ? c => c.gameObject.SetActive(false) : c => c.enabled = false;
			foreach (VisualEffect vfx in vfxAll)
			{
				disableOrDeactivate(vfx);
			}
			foreach (Tuple<LightFlickerSynced, Light2D, float> light in lights)
			{
				disableOrDeactivate(light.Item2);
			}
		}
	}

	private const System.Reflection.BindingFlags m_nonpublicWorkaroundFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

	public static object NonpublicGetterWorkaround(this object component, string fieldName) => component.GetType().GetField(fieldName, m_nonpublicWorkaroundFlags).GetValue(component);

	public static void NonpublicSetterWorkaround(this object component, string fieldName, object value)
	{
		// see https://forum.unity.com/threads/lwrp-light-2d-change-sprite-in-script.753542/ for explanation of workaround for serialized properties not having public setters
		System.Reflection.FieldInfo setterWorkaround = component.GetType().GetField(fieldName, m_nonpublicWorkaroundFlags);
		setterWorkaround.SetValue(component, value);
	}


	private static T WeightedObjectToObject<T>(WeightedObject<T> pair) => pair.m_object;
}
