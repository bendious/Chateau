using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;


[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public class LockController : MonoBehaviour, IUnlockable
{
	[Serializable] public struct KeyInfo
	{
		public Sprite[] m_doorSprites;
		public WeightedObject<WeightedObject<GameObject>>[] m_prefabs; // NOTE that the inner weights are used as per-instance difficulty values // TODO: named struct for clarity?
		public WeightedObject<WeightedObject<GameObject>>[] m_orderPrefabs;
		public int m_keyCountMax; // TODO: specify min as well as max?
		public int m_combinationDigitsMin;
		public int m_combinationDigitsMax;
		[SerializeField] internal bool m_genericKeys;

		internal Vector2 DifficultyRange(int keyRoomCount, Vector2 combinationDifficultyRange) => Mathf.Min(keyRoomCount, m_keyCountMax) * (m_prefabs.MinMax(prefab => prefab.m_object.m_weight) + m_orderPrefabs.MinMax(prefab => prefab.m_object.m_weight) + (m_combinationDigitsMax <= 0 ? Vector2.zero : combinationDifficultyRange));
	}
	[Serializable] public class CombinationSet
	{
		[Serializable]
		public class Option
		{
			public string[] m_strings;
			public Sprite m_sprite;
		}
		public Option[] m_options;
		public float m_difficulty;
		public float m_spriteUsagePct = 0.0f;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;
	public WeightedObject<CombinationSet>[] m_combinationSets;

	public float m_difficulty;
	[SerializeField] private int m_builtinKeysMin;
	[SerializeField] private int m_builtinKeysMax;
	public int m_builtinKeyDifficulty;
	public float m_keyHeightMax = 7.5f;
	[SerializeField] private float m_keySpeedMin = 0.0f;
	[SerializeField] private float m_keyDelaySeconds = 0.0f;
	[SerializeField] private float m_vfxDisableDelaySeconds = 2.0f;
	[SerializeField] private VisualEffect m_vfxKeyDelay;

	[SerializeField] private WeightedObject<AudioClip>[] m_failureSFX;
	[SerializeField] private WeightedObject<AudioClip>[] m_unlockSFX;
	[SerializeField] private WeightedObject<GameObject>[] m_unlockVFX;

	[SerializeField] private float m_activeColorPct = 2.0f;


	[SerializeField] private float m_unlockDestroyDelay = -1.0f;


	public GameObject Parent { get; set; }
	public bool IsLocked => enabled;

	public bool IsCriticalPath { private get; set; }


	private readonly List<Tuple<IKey, GameObject>> m_keys = new();
#if DEBUG
	private bool m_debugHasSpawnedKeys = false;
#endif
	private KeyInfo m_keyInfo;
	private float m_difficultyAvgPerItem = -1.0f;
	private CombinationSet m_combinationSet;
	private bool m_hasTrigger;

	private int m_unlockInProgressCount;


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
#if DEBUG
		Debug.Assert(!m_debugHasSpawnedKeys); // NOTE that we can't just check m_keys due to the possibility of both spawned and within-prefab keys
		m_debugHasSpawnedKeys = true;
#endif

		// determine key type
		int keyRoomCount = keyRooms == null ? 0 : keyRooms.Length;
		RoomController[] keyOrLockRooms = keyRoomCount > 0 ? keyRooms : new[] { lockRoom };
		if (m_keyPrefabs.Length > 0)
		{
			m_keyInfo = RoomController.RandomWeightedByKeyCount(m_keyPrefabs.CombineWeighted(GameController.Instance.m_keyPrefabs, info => info.m_object.m_prefabs.Select(info => info.m_object.m_object).FirstOrDefault(prefab => GameController.Instance.m_keyPrefabs.Any(key => key.m_object == prefab)), pair => pair.m_object), ToKeyStats, keyRoomCount, difficultyPct);
		}

		// calculate difficulty
		float difficultyDesired = Mathf.Lerp(GameController.Instance.m_difficultyMin, GameController.Instance.m_difficultyMax, difficultyPct);
		int keyCount = Math.Min(keyOrLockRooms.Length, m_keyInfo.m_keyCountMax);
		int combinationLen = m_keyInfo.m_combinationDigitsMax <= 0 ? 0 : Mathf.Max(keyCount, m_keyInfo.m_combinationDigitsMin, Mathf.Min(m_keyInfo.m_combinationDigitsMax, Mathf.RoundToInt(difficultyDesired - keyCount))); // NOTE that the combination length can't be less but can be more than the number of keys since multiple digits can be indicated by a single key // NOTE also that we choose a combination length such that per-item difficulty stays close to 1.0 // TODO: random variance?
		m_difficultyAvgPerItem = difficultyDesired / Math.Max(1, keyCount + combinationLen); // TODO: don't assume keys and combinations will have the same average difficulty?

		if (keyRooms == null || keyCount <= 0)
		{
			return; // NOTE that this is valid in the Entryway, where locks are spawned w/o keys
		}

		// spawn key(s)
		const float scalarPerDiff = 0.5f; // TODO: parameterize?
		float[] weightsEdited = m_keyInfo.m_prefabs.Select(info => info.m_weight / (1.0f + scalarPerDiff * Mathf.Abs(info.m_object.m_weight - m_difficultyAvgPerItem))).ToArray();
		// TODO: convert any empty key rooms into bonus item rooms?
		for (int i = 0; i < keyCount; ++i)
		{
			GameObject keyPrefab = m_keyInfo.m_prefabs.RandomWeighted(weightsEdited).m_object.m_object;
			GameObject keyObj = keyOrLockRooms[i].SpawnKey(keyPrefab, m_keyHeightMax, false, IsCriticalPath);
			foreach (IKey key in keyObj.GetComponentsInChildren<IKey>())
			{
				key.Lock = this;
				key.SetDesiredDifficulty(m_difficultyAvgPerItem);
				m_keys.Add(Tuple.Create(key, keyObj));
			}
		}

		// setup key(s)
		// TODO: move into Start() to ensure late-added keys (e.g. puzzle pieces) are present?
		if (combinationLen > 0)
		{
			// assign combination
			float[] comboWeightsEdited = m_combinationSets.Select(set => set.m_weight / (1.0f + scalarPerDiff * Mathf.Abs(set.m_object.m_difficulty - m_difficultyAvgPerItem))).ToArray();
			m_combinationSet = m_combinationSets.RandomWeighted(comboWeightsEdited).m_object;
			int[] combination = new int[combinationLen];
			for (int digitIdx = 0; digitIdx < combinationLen; ++digitIdx)
			{
				combination[digitIdx] = UnityEngine.Random.Range(0, m_combinationSet.m_options.Length); // TODO: recognize & act upon "special" combinations (0333, 0666, real words, etc.)?
			}

			// disable excess combination keys
			int keyCountLocal = m_keys.Count(key => key.Item2.GetComponentsInParent<Transform>().Contains(transform)); // TODO: efficiency?
			if (combinationLen < keyCountLocal)
			{
				for (int i = combinationLen; i < m_keyInfo.m_combinationDigitsMax; ++i)
				{
					m_keys[i].Item1.Component.gameObject.SetActive(false);
				}
			}

			// distribute combination among keys/children
			bool useSprites = false;
			int optionsCount = m_combinationSet.m_options.First().m_strings.Length; // TODO: don't assume all options have the same m_strings.Length?
			int optionIdx = -1;
			float digitsPerKey = (float)combinationLen / keyCount;
			int comboIdx = 0;
			int keyIdx = -2; // -2 since the incrementing is done before the first SetCombination() call and since the first key object is generally the non-deactivated lock // TODO: robustness?
			GameObject keyObjPrev = null;
			foreach (Tuple<IKey, GameObject> key in m_keys)
			{
				// determine how much of the combination this key gets
				if (key.Item2 != keyObjPrev) // TODO: don't assume m_keys[] is ordered?
				{
					useSprites = m_combinationSet.m_spriteUsagePct > 0.0f && UnityEngine.Random.value <= m_combinationSet.m_spriteUsagePct; // NOTE the prevention of rare unexpected results when usage percent is 0 or 1
					optionIdx = UnityEngine.Random.Range(0, optionsCount);
					comboIdx = 0;
					keyIdx = (keyIdx + 1) % combination.Length;
				}
				float startIdxF = (keyIdx * digitsPerKey) % combination.Length;
				int startIdx = Mathf.RoundToInt(startIdxF);
				int endIdx = Mathf.RoundToInt(startIdxF + digitsPerKey);

				// pass to key component
				key.Item1.SetCombination(m_combinationSet, combination, optionIdx, combination[comboIdx], startIdx, endIdx, useSprites);
				if (!key.Item1.Component.isActiveAndEnabled && (comboIdx < startIdx || comboIdx >= endIdx))
				{
					key.Item1.Component.gameObject.SetActive(false);
				}

				if (keyIdx < 0 && !key.Item1.Component.isActiveAndEnabled) // TODO: better check for excess combination keys?
				{
					// NOTE that we don't iterate comboIdx since this key must be in excess of the combination length
					continue;
				}

				// iterate
				comboIdx = (comboIdx + 1) % combination.Length;
				keyObjPrev = key.Item2;
			}
		}

		// spawn order guide if applicable
		GameObject orderObj = null;
		if (m_keys.Count > 1 && m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			int spawnRoomIdx = UnityEngine.Random.Range(0, keyOrLockRooms.Length + 1);
			RoomController spawnRoom = spawnRoomIdx >= keyOrLockRooms.Length ? lockRoom : keyOrLockRooms[spawnRoomIdx];
			float[] orderWeightsEdited = m_keyInfo.m_orderPrefabs.Select(info => info.m_weight / (1.0f + scalarPerDiff * Mathf.Abs(info.m_object.m_weight - m_difficultyAvgPerItem))).ToArray();
			GameObject orderPrefab = m_keyInfo.m_orderPrefabs.RandomWeighted(orderWeightsEdited).m_object.m_object;
			Vector3 spawnPos = spawnRoom.InteriorPosition(m_keyHeightMax, orderPrefab);
			orderObj = Instantiate(orderPrefab, spawnPos, Quaternion.identity, spawnRoom.transform);

			// disable extra order objects
			SpriteRenderer[] orderRenderers = orderObj.GetComponentsInChildren<SpriteRenderer>();
			for (int i = m_keys.Count; i < orderRenderers.Length; ++i) // TODO: don't assume all of m_keys[] should be counted?
			{
				orderRenderers[i].gameObject.SetActive(false);
			}
		}

		// allow duplicates in ordered keys
		// TODO: re-implement after upgrading IKey.IsInPlace? allow duplicates before some originals?

		// match key color(s)
		SpriteRenderer[] childKeyRenderers = (orderObj != null ? orderObj : gameObject).GetComponentsInChildren<ColorRandomizer>().Select(randomizer => randomizer.GetComponentInChildren<SpriteRenderer>()).ToArray();
		SpriteRenderer[] spawnedKeyRenderers = m_keys.Select(key => key.Item1.Component.GetComponent<SpriteRenderer>()).Where(r => r != null && !childKeyRenderers.Contains(r)).ToArray();
		int colorCount = Math.Min(childKeyRenderers.Length, spawnedKeyRenderers.Count());
		if (colorCount > 0)
		{
			IEnumerable<SpriteRenderer> renderers = childKeyRenderers.Concat(spawnedKeyRenderers);
			Color[] colors = renderers.Where(r => r.GetComponentInParent<ColorRandomizer>() != null).Select(r => r.color).ToArray(); // TODO: ensure separate colors are visually distinct?
			int keyColorIdx = 0;
			foreach (SpriteRenderer renderer in renderers)
			{
				Color color = colors[keyColorIdx % colorCount];
				foreach (SpriteRenderer child in renderer.GetComponentsInChildren<SpriteRenderer>())
				{
					child.color = color;
				}
				++keyColorIdx; // TODO: take multi-color composite keys into account?
			}
		}

		// door setup based on keys
		UpdateSprite();
	}

	public bool IsValidNextKey(GameObject obj)
	{
		if (!enabled)
		{
			return false; // prevent consuming generic keys after already unlocked
		}

		if (m_keySpeedMin <= 0.0f && m_keyDelaySeconds <= 0.0f && obj.TryGetComponent(out ItemController item) && (item.Cause == null || item.Cause is not AvatarController))
		{
			return false; // prevent NPC-held/loose keys unlocking things by accident
		}

		bool CheckKey(IKey key)
		{
			if (key == null || key.Component == null)
			{
				return false;
			}
			if (m_keySpeedMin > 0.0f)
			{
				if (obj.transform.parent != null)
				{
					return false;
				}
				Rigidbody2D body = obj.GetComponent<Rigidbody2D>();
				if (body == null || body.velocity.magnitude < m_keySpeedMin)
				{
					return false;
				}
			}
			if (key.Component.gameObject == obj)
			{
				return true;
			}
			if (!m_keyInfo.m_genericKeys)
			{
				return false;
			}
			ISavable savableObj = obj.GetComponent<ISavable>();
			ISavable savableKey = key.Component.GetComponent<ISavable>();
			return savableObj != null && savableKey != null && savableObj.Type != -1 && (savableObj.Type == savableKey.Type || savableObj.Type == Array.FindIndex(GameController.Instance.m_savableFactory.m_savables, info => info.m_prefab == key.Component.gameObject)); // NOTE the extra check of SavableFactory.m_savables since the key might be a pre-spawned prefab
		}

		int matchingKeyIdx = -1;
		bool retVal = false;
		if (m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			// ordered
			matchingKeyIdx = m_keys.FindIndex(key => !key.Item1.IsInPlace);
			retVal = matchingKeyIdx >= 0 && (CheckKey(m_keys[matchingKeyIdx].Item1) || (m_keyInfo.m_genericKeys && m_keyInfo.m_prefabs.Length > matchingKeyIdx && CheckKey(m_keyInfo.m_prefabs[matchingKeyIdx].m_object.m_object.GetComponent<IKey>())));
		}
		else
		{
			// unordered
			matchingKeyIdx = m_keys.FindIndex(key => CheckKey(key.Item1));
			retVal = matchingKeyIdx >= 0 || (m_keyInfo.m_genericKeys && m_keyInfo.m_prefabs.Any(keyPrefab => CheckKey(keyPrefab.m_object.m_object.GetComponent<IKey>())));
		}

		// swap out generic keys if necessary
		if (retVal && m_keyInfo.m_genericKeys && matchingKeyIdx >= 0)
		{
			IKey newKey = obj.GetComponent<IKey>();
			if (m_keys[matchingKeyIdx] != newKey)
			{
				m_keys[matchingKeyIdx] = Tuple.Create(newKey, obj);
			}
		}

		return retVal;
	}

	public struct KeyStats
	{
		public Vector2Int m_keyRoomsMinMax;
		public Vector2 m_difficultyMinMax;

		public static KeyStats Invalid => new() { m_keyRoomsMinMax = new(int.MaxValue, 0), m_difficultyMinMax = new(float.MaxValue, 0.0f) };
		public bool IsValid => m_keyRoomsMinMax.x <= m_keyRoomsMinMax.y && m_difficultyMinMax.x <= m_difficultyMinMax.y;
		public KeyStats Aggregate(KeyStats rhs) => new() { m_keyRoomsMinMax = new(Math.Min(m_keyRoomsMinMax.x, rhs.m_keyRoomsMinMax.x), Math.Max(m_keyRoomsMinMax.y, rhs.m_keyRoomsMinMax.y)), m_difficultyMinMax = new(Mathf.Min(m_difficultyMinMax.x, rhs.m_difficultyMinMax.x), Mathf.Max(m_difficultyMinMax.y, rhs.m_difficultyMinMax.y)) };
	};

	public KeyStats ToKeyStats(int keyRoomCount) => m_keyPrefabs.Length <= 0 ? new() { m_difficultyMinMax = new Vector2(m_builtinKeysMin, m_builtinKeysMax) * m_builtinKeyDifficulty } : m_keyPrefabs.Aggregate(KeyStats.Invalid, (total, nextInfo) => total.Aggregate(ToKeyStats(nextInfo.m_object, keyRoomCount)));

	private KeyStats ToKeyStats(KeyInfo info, int keyRoomCount) => new() { m_keyRoomsMinMax = new(info.m_keyCountMax, info.m_keyCountMax), m_difficultyMinMax = new Vector2(m_difficulty, m_difficulty) + new Vector2(m_builtinKeysMin, m_builtinKeysMax) * m_builtinKeyDifficulty + info.DifficultyRange(keyRoomCount, m_combinationSets.MinMax(set => set.m_object.m_difficulty)) };

	public bool CheckInput()
	{
		foreach (Tuple<IKey, GameObject> key in m_keys)
		{
			if (!key.Item1.IsInPlace)
			{
				// NOTE that we don't play m_failureSFX since this is called by InteractToggle() every time the input is toggled
				return false;
			}
		}

		Unlock(null);
		return true;
	}


	private void Awake()
	{
		m_keys.AddRange(GetComponentsInChildren<IKey>().Select(key => Tuple.Create(key, gameObject))); // NOTE that we use our own game object for in-prefab keys

		m_hasTrigger = GetComponents<Collider2D>().Any(collider => collider.isTrigger);
	}

	private void Start()
	{
		bool recheckKeys = true;
		while (recheckKeys) // due to InteractFollow.SetDesiredDifficulty() potentially adding new keys // TODO: refactor?
		{
			recheckKeys = false;
			foreach (IKey key in GetComponentsInChildren<IKey>())
			{
				key.Lock = this;
				recheckKeys |= key.SetDesiredDifficulty(m_difficultyAvgPerItem);
				if (!m_keys.Any(pair => pair.Item1 == key))
				{
					m_keys.Add(Tuple.Create(key, key.Component.gameObject)); // NOTE that we use the key's game object for late-added keys
				}
			}
		}
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (m_combinationSet != null)
		{
			return;
		}

		foreach (Transform tf in m_keyDelaySeconds > 0.0f ? new[] { collider.transform } : collider.GetComponentsInChildren<Transform>(true)) // NOTE that keys w/ delays require direct trigger contact
		{
			if (IsValidNextKey(tf.gameObject))
			{
				StartCoroutine(KeyUnlockDelayed(tf));
				if (!enabled)
				{
					break; // prevent consuming generic keys after already unlocked
				}
			}
		}
	}

	private void OnTriggerExit2D(Collider2D collider)
	{
		// NOTE that keys w/ delays require direct trigger contact
		if (m_unlockInProgressCount > 0 && IsValidNextKey(collider.gameObject)) // TODO: cache currently in-progress key objects?
		{
			--m_unlockInProgressCount;
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (m_hasTrigger)
		{
			return;
		}
		OnTriggerEnter2D(collision.collider);
	}

	private void OnCollisionExit2D(Collision2D collision)
	{
		if (m_hasTrigger)
		{
			return;
		}
		OnTriggerExit2D(collision.collider);
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad)
		{
			return;
		}

		foreach (Tuple<IKey, GameObject> key in m_keys)
		{
			key.Item1.Deactivate();
		}
		if (Parent != null)
		{
			Parent.GetComponent<IUnlockable>().Unlock(null);
		}
	}


#if DEBUG
	public
#else
	private
#endif
		bool Unlock(IKey key, bool silent = false)
	{
		// handle given key
		AudioSource audio = silent ? null : GetComponent<AudioSource>();
		if (key != null)
		{
			if (!IsValidNextKey(key.Component.gameObject)) // TODO: move to ButtonController?
			{
				// TODO: visual indication of failure?
				if (!silent)
				{
					audio.clip = m_failureSFX.RandomWeighted();
					audio.time = 0.0f;
					audio.Play();
				}

				foreach (Tuple<IKey, GameObject> entry in m_keys)
				{
					entry.Item1.Cancel();
				}
				UpdateSprite();

				return false;
			}

			key.IsInPlace = true;
		}

		// check for full unlocking
		int remainingKeys = m_keys.Count(key => !key.Item1.IsInPlace);
		if (key == null || remainingKeys <= 0)
		{
			// unlock parent
			IUnlockable parentLock = Parent == null ? null : Parent.GetComponent<IUnlockable>();
			parentLock?.Unlock(key, silent);

			if (!silent)
			{
				GameController.Instance.AddCameraTargets(transform);
			}

			// destroy/disable ourself
			if (m_unlockDestroyDelay > 0.0f)
			{
				StartCoroutine(this.GateDespawnAnimationCoroutine(m_unlockDestroyDelay));
			}
			else if (m_unlockDestroyDelay == 0.0f)
			{
				Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
			}
			else
			{
				Simulation.Schedule<CameraTargetRemove>(1.0f).m_transform = transform; // TODO: parameterize? guarantee camera reaches us?

				if (TryGetComponent(out Hazard hazard))
				{
					hazard.enabled = !hazard.enabled;
				}
				if (TryGetComponent(out Collider2D collider))
				{
					collider.enabled = hazard != null && hazard.enabled;
				}
				if (TryGetComponent(out VisualEffect vfx))
				{
					if (vfx.enabled)
					{
						StartCoroutine(vfx.gameObject.SoftStop(delayMax: m_vfxDisableDelaySeconds, postBehavior: Utility.SoftStopPost.DisableComponents)); // disable after short delay to prevent existing particles remaining while off-screen and becoming visible once not culled
					}
					else
					{
						vfx.enabled = true;
						vfx.Play();
					}
				}
				if (TryGetComponent(out Light2D light))
				{
					light.enabled = !light.enabled; // TODO: disable as part of vfx.SoftStop() by splitting lights and VFX onto their own object
				}
				if (TryGetComponent(out LightFlicker lightFlicker))
				{
					lightFlicker.enabled = !lightFlicker.enabled;
				}

				if (TryGetComponent(out LineRenderer line))
				{
					line.colorGradient = new() { colorKeys = new GradientColorKey[] { new(GetComponent<SpriteRenderer>().color * m_activeColorPct, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new(1.0f, 0.0f) } };
				}
			}

			Parent = null;
			enabled = false;

			// deactivate keys
			foreach (Tuple<IKey, GameObject> keyEntry in m_keys)
			{
				if (keyEntry.Item1 == null || keyEntry.Item1.Component == null)
				{
					continue;
				}
				keyEntry.Item1.IsInPlace = true;
				keyEntry.Item1.Deactivate();
			}
			m_keys.Clear();
		}

		// update sprite
		// TODO: support arbitrary key placement?
		UpdateSprite();

		// TODO: unlock animation/etc.
		if (!silent)
		{
			if (m_unlockSFX.Length > 0)
			{
				audio.clip = m_unlockSFX.RandomWeighted();
				if (m_unlockDestroyDelay <= 0.0f)
				{
					AudioSource.PlayClipAtPoint(audio.clip, transform.position); // NOTE that we can't use our own audio source if we're being despawned immediately
				}
				else
				{
					audio.time = 0.0f;
					audio.Play();
				}
			}
			if (m_unlockVFX.Length > 0)
			{
				Instantiate(m_unlockVFX.RandomWeighted(), transform.position, transform.rotation);
			}
		}

		return true;
	}

	private void UpdateSprite()
	{
		if (m_keyInfo.m_doorSprites != null && m_keyInfo.m_doorSprites.Length > 0)
		{
			GetComponent<SpriteRenderer>().sprite = m_keyInfo.m_doorSprites[^Mathf.Min(m_keyInfo.m_doorSprites.Length, m_keys.Count(key => !key.Item1.IsInPlace) + 1)];
		}
	}

	private IEnumerator KeyUnlockDelayed(Transform tf)
	{
		++m_unlockInProgressCount;
		if (m_keyDelaySeconds > 0.0f)
		{
			if (m_vfxKeyDelay != null)
			{
				m_vfxKeyDelay.gameObject.SetActive(true);
				m_vfxKeyDelay.enabled = true;
				m_vfxKeyDelay.Play();
			}

			float unlockTime = Time.time + m_keyDelaySeconds;
			yield return new WaitUntil(() => m_unlockInProgressCount == 0 || Time.time >= unlockTime);
		}

		if (m_unlockInProgressCount == 0)
		{
			if (m_vfxKeyDelay != null)
			{
				StartCoroutine(m_vfxKeyDelay.gameObject.SoftStop(() => m_unlockInProgressCount > 0, m_vfxDisableDelaySeconds, Utility.SoftStopPost.DisableComponents));
			}
			yield break;
		}
		m_unlockInProgressCount = 0;

		IKey key = tf.GetComponent<IKey>();
		Unlock(key); // NOTE that this has to be BEFORE key.Use() since that might detach the item and lose the cause before it is verified
		key.Use();
	}
}
