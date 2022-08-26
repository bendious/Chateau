using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
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
		public int m_combinationDigits;
		[SerializeField] internal bool m_genericKeys;

		internal Vector2 DifficultyRange(int keyRoomCount, Vector2 combinationDifficultyRange) => new Vector2(Mathf.Min(keyRoomCount, m_keyCountMax), Mathf.Min(keyRoomCount, m_keyCountMax)) * m_prefabs.MinMax(prefab => prefab.m_object.m_weight) + m_orderPrefabs.MinMax(prefab => prefab.m_object.m_weight) + (m_combinationDigits <= 0 ? Vector2.zero : combinationDifficultyRange);
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
	public float m_keyHeightMax = 7.5f;
	[SerializeField] private float m_keyDelaySeconds = 0.0f;
	[SerializeField] private float m_vfxDisableDelaySeconds = 2.0f;

	public WeightedObject<AudioClip>[] m_failureSFX;
	public WeightedObject<AudioClip>[] m_unlockSFX;

	[SerializeField] private float m_activeColorPct = 2.0f;


	[SerializeField] private bool m_destroyOnUnlock = true;


	public GameObject Parent { get; set; }
	public bool IsLocked => enabled;


	private readonly List<Tuple<IKey, GameObject>> m_keys = new();
#if DEBUG
	private bool m_debugHasSpawnedKeys = false;
#endif
	private KeyInfo m_keyInfo;
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

		if (keyRooms == null || m_keyInfo.m_keyCountMax <= 0)
		{
			return; // NOTE that this is valid in the Entryway, where locks are spawned w/o keys
		}

		// calculate difficulty
		float difficultyDesired = Mathf.Lerp(GameController.Instance.m_difficultyMin, GameController.Instance.m_difficultyMax, difficultyPct);
		int keyCount = Math.Min(keyOrLockRooms.Length, m_keyInfo.m_keyCountMax);
		float difficultyAvgPerItem = difficultyDesired / keyCount;
		const float scalarPerDiff = 0.5f; // TODO: parameterize?
		float[] weightsEdited = m_keyInfo.m_prefabs.Select(info => info.m_weight / (1.0f + scalarPerDiff * Mathf.Abs(info.m_object.m_weight - difficultyAvgPerItem))).ToArray();

		// spawn key(s)
		// TODO: convert any empty key rooms into bonus item rooms?
		for (int i = 0; i < keyCount; ++i)
		{
			GameObject keyPrefab = m_keyInfo.m_prefabs.RandomWeighted(weightsEdited).m_object.m_object;
			GameObject keyObj = keyOrLockRooms[i].SpawnKey(keyPrefab, m_keyHeightMax, false);
			foreach (IKey key in keyObj.GetComponentsInChildren<IKey>())
			{
				key.Lock = this;
				m_keys.Add(Tuple.Create(key, keyObj));
			}
		}

		// setup key(s)
		// TODO: move into Start() to ensure late-added keys (e.g. puzzle pieces) are present?
		if (m_keyInfo.m_combinationDigits > 0)
		{
			// assign combination
			m_combinationSet = m_combinationSets.RandomWeighted();
			int[] combination = new int[m_keyInfo.m_combinationDigits];
			for (int digitIdx = 0; digitIdx < m_keyInfo.m_combinationDigits; ++digitIdx)
			{
				combination[digitIdx] = UnityEngine.Random.Range(0, m_combinationSet.m_options.Length); // TODO: recognize & act upon "special" combinations (0333, 0666, real words, etc.)?
			}

			// distribute combination among keys/children
			bool useSprites = false;
			int optionsCount = m_combinationSet.m_options.First().m_strings.Length; // TODO: don't assume all options have the same m_strings.Length?
			int optionIdx = -1;
			float digitsPerKey = (float)m_keyInfo.m_combinationDigits / keyCount;
			int comboIdx = 0;
			int keyIdx = -1;
			GameObject keyObjPrev = null;
			foreach (Tuple<IKey, GameObject> key in m_keys)
			{
				// determine how much of the combination this key gets
				if (key.Item2 != keyObjPrev) // TODO: don't assume m_keys[] is ordered?
				{
					useSprites = m_combinationSet.m_spriteUsagePct > 0.0f && UnityEngine.Random.value <= m_combinationSet.m_spriteUsagePct; // NOTE the prevention of rare unexpected results when usage percent is 0 or 1
					optionIdx = UnityEngine.Random.Range(0, optionsCount);
					keyIdx = (keyIdx + 1) % combination.Length;
				}
				float startIdxF = (keyIdx * digitsPerKey) % combination.Length;
				int startIdx = Mathf.RoundToInt(startIdxF);
				int endIdx = Mathf.RoundToInt(startIdxF + digitsPerKey);

				// pass to key component
				key.Item1.SetCombination(m_combinationSet, combination, optionIdx, combination[comboIdx], startIdx, endIdx, useSprites);
				if (!key.Item1.Component.enabled && (comboIdx < startIdx || comboIdx >= endIdx))
				{
					key.Item1.Component.gameObject.SetActive(false);
				}

				// iterate
				comboIdx = (comboIdx + 1) % combination.Length;
				keyObjPrev = key.Item2;
			}
		}

		// spawn order guide if applicable
		float[] orderWeightsEdited = m_keyInfo.m_orderPrefabs.Select(info => info.m_weight / (1.0f + scalarPerDiff * Mathf.Abs(info.m_object.m_weight - difficultyAvgPerItem))).ToArray();
		GameObject orderObj = null;
		if (m_keys.Count > 1 && m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			int spawnRoomIdx = UnityEngine.Random.Range(0, keyOrLockRooms.Length + 1);
			RoomController spawnRoom = spawnRoomIdx >= keyOrLockRooms.Length ? lockRoom : keyOrLockRooms[spawnRoomIdx];
			GameObject orderPrefab = m_keyInfo.m_orderPrefabs.RandomWeighted(orderWeightsEdited).m_object.m_object;
			Vector3 spawnPos = spawnRoom.InteriorPosition(m_keyHeightMax, orderPrefab);
			orderObj = Instantiate(orderPrefab, spawnPos, Quaternion.identity, spawnRoom.transform);
		}

		// allow duplicates in ordered keys
		// TODO: re-implement after upgrading IKey.IsInPlace? allow duplicates before some originals?

		// match key color(s)
		// TODO: base on ColorRandomizer?
		SpriteRenderer[] childKeyRenderers = (orderObj != null ? orderObj : gameObject).GetComponentsInChildren<IKey>().Select(key => key.Component.GetComponent<SpriteRenderer>()).Where(r => r != null).ToArray();
		IEnumerable<SpriteRenderer> spawnedKeyRenderers = m_keys.Select(key => key.Item1.Component.GetComponent<SpriteRenderer>()).Where(r => r != null && !childKeyRenderers.Any(nonspawned => nonspawned.gameObject == r.gameObject)).ToArray();
		if (childKeyRenderers.Length == 0)
		{
			// single-color self and keys
			Color color = spawnedKeyRenderers.First().color;
			GetComponent<SpriteRenderer>().color = color;
			foreach (SpriteRenderer r in spawnedKeyRenderers)
			{
				r.color = color;
			}
		}
		else if (spawnedKeyRenderers.Count() > 0)
		{
			// multi-color children according to keys
			if (spawnedKeyRenderers.First().GetComponentInChildren<TMP_Text>() != null) // TODO: better determination of whether puzzles need to auto-disable extra child keys?
			{
				int i = 0;
				foreach (SpriteRenderer childR in childKeyRenderers)
				{
					childR.color = spawnedKeyRenderers.ElementAt(i * spawnedKeyRenderers.Count() / childKeyRenderers.Length).color;
					++i;
				}
			}
			else
			{
				// color match children one-to-one
				int i = 0;
				for (; i < childKeyRenderers.Length && i < spawnedKeyRenderers.Count(); ++i)
				{
					SpriteRenderer spawnedR = spawnedKeyRenderers.ElementAt(i);

					// ensure colors are visually distinct
					foreach (SpriteRenderer spawnedR2 in spawnedKeyRenderers)
					{
						if (spawnedR == spawnedR2)
						{
							break;
						}
						if (!spawnedR.color.ColorsSimilar(spawnedR2.color))
						{
							continue;
						}
						spawnedR.color = spawnedR.color.ColorFlipComponent(UnityEngine.Random.Range(0, 3), Color.black, Color.white); // TODO: use ColorRandomizer range? ensure flipping component doesn't result in conflict w/ any previous color?
					}

					childKeyRenderers[i].color = spawnedR.color;
				}

				// match extra spawned colors
				for (; i < spawnedKeyRenderers.Count(); ++i)
				{
					spawnedKeyRenderers.ElementAt(i).color = childKeyRenderers[i % childKeyRenderers.Length].color;
				}

				// auto-disable extra children
				for (; i < childKeyRenderers.Length; ++i)
				{
					childKeyRenderers[i].gameObject.SetActive(false);
				}
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

		bool CheckKey(IKey key)
		{
			if (key == null || key.Component == null)
			{
				return false;
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

	public Tuple<Vector2Int, Vector2> ToKeyStats(int keyRoomCount) => m_keyPrefabs.Length <= 0 ? Tuple.Create(Vector2Int.zero, Vector2.zero) : m_keyPrefabs.Aggregate(Tuple.Create(new Vector2Int(int.MaxValue, 0), new Vector2(float.MaxValue, 0.0f)), (total, nextInfo) =>
	{
		Tuple<Vector2Int, Vector2> nextStats = ToKeyStats(nextInfo.m_object, keyRoomCount);
		return Tuple.Create(new Vector2Int(Math.Min(total.Item1.x, nextStats.Item1.x), Math.Max(total.Item1.y, nextStats.Item1.y)), new Vector2(Mathf.Min(total.Item2.x, nextStats.Item2.x), Mathf.Max(total.Item2.y, nextStats.Item2.y)));
	});

	private Tuple<Vector2Int, Vector2> ToKeyStats(KeyInfo info, int keyRoomCount) => Tuple.Create(new Vector2Int(info.m_keyCountMax, info.m_keyCountMax), new Vector2(m_difficulty, m_difficulty) + info.DifficultyRange(keyRoomCount, m_combinationSets.MinMax(set => set.m_object.m_difficulty)));

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
		foreach (IKey key in GetComponentsInChildren<IKey>())
		{
			key.Lock = this;
			if (!m_keys.Any(pair => pair.Item1 == key))
			{
				m_keys.Add(Tuple.Create(key, key.Component.gameObject)); // NOTE that we use the key's game object for late-added keys
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
			if (parentLock != null)
			{
				parentLock.Unlock(key, silent);
			}

			if (!silent)
			{
				GameController.Instance.AddCameraTargets(transform);
			}

			// destroy/disable ourself
			if (m_destroyOnUnlock)
			{
				Simulation.Schedule<ObjectDespawn>(0.5f).m_object = gameObject;
			}
			else
			{
				Simulation.Schedule<CameraTargetRemove>(1.0f).m_transform = transform; // TODO: guarantee camera reaches us?

				Hazard hazard = GetComponent<Hazard>();
				if (hazard != null)
				{
					hazard.enabled = !hazard.enabled;
				}
				Collider2D collider = GetComponent<Collider2D>();
				if (collider != null)
				{
					collider.enabled = hazard != null && hazard.enabled;
				}
				VisualEffect vfx = GetComponent<VisualEffect>();
				if (vfx != null)
				{
					if (vfx.enabled)
					{
						vfx.Stop();
						StartCoroutine(DisableDelayed(m_vfxDisableDelaySeconds, vfx)); // disable after short delay to prevent existing particles remaining while off-screen and becoming visible once not culled
					}
					else
					{
						vfx.enabled = true;
						vfx.Play();
					}
				}
				Light2D light = GetComponent<Light2D>();
				if (light != null)
				{
					light.enabled = !light.enabled;
				}
				LightFlicker lightFlicker = GetComponent<LightFlicker>();
				if (lightFlicker != null)
				{
					lightFlicker.enabled = !lightFlicker.enabled;
				}

				LineRenderer line = GetComponent<LineRenderer>();
				if (line != null)
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

		// TODO: unlock animation/VFX/etc.
		if (!silent)
		{
			audio.clip = m_unlockSFX.RandomWeighted();
			audio.time = 0.0f;
			audio.Play();
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
			float unlockTime = Time.time + m_keyDelaySeconds;
			yield return new WaitUntil(() => m_unlockInProgressCount == 0 || Time.time >= unlockTime);
		}

		if (m_unlockInProgressCount == 0)
		{
			yield break;
		}
		m_unlockInProgressCount = 0;

		IKey key = tf.GetComponent<IKey>();
		key.Use();
		Unlock(key);
	}

	private IEnumerator DisableDelayed(float delay, Behaviour component)
	{
		yield return new WaitForSeconds(delay);
		component.enabled = false;
	}
}
