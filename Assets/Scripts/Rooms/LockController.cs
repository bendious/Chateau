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
	[System.Serializable]
	public struct KeyInfo
	{
		public Sprite[] m_doorSprites;
		public WeightedObject<GameObject>[] m_prefabs;
		public WeightedObject<GameObject>[] m_orderPrefabs;
		public int m_keyCountMax;
		public int m_combinationDigits;
		[SerializeField] internal bool m_genericKeys;
	}
	[System.Serializable] public class CombinationSet
	{
		[System.Serializable]
		public class Option
		{
			public string[] m_strings;
			public Sprite m_sprite;
		}
		public Option[] m_options;
		public float m_spriteUsagePct = 0.0f;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;
	public WeightedObject<CombinationSet>[] m_combinationSets;

	public float m_keyHeightMax = 7.5f;
	[SerializeField] private float m_keyDelaySeconds = 0.0f;
	[SerializeField] private float m_vfxDisableDelaySeconds = 2.0f;

	public WeightedObject<AudioClip>[] m_failureSFX;
	public WeightedObject<AudioClip>[] m_unlockSFX;


	[SerializeField]
	private bool m_destroyOnUnlock = true;


	public GameObject Parent { get; set; }

	public bool HasKeys => m_keys.Count > 0;


	private readonly List<IKey> m_keys = new();
	private KeyInfo m_keyInfo;
	private CombinationSet m_combinationSet;
	private bool m_hasTrigger;

	private bool m_unlockInProgress;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		// determine key type
		int keyRoomCount = keyRooms == null ? 0 : keyRooms.Length;
		RoomController[] keyOrLockRooms = keyRoomCount > 0 ? keyRooms : new RoomController[] { lockRoom };
		if (m_keyPrefabs.Length > 0)
		{
			m_keyInfo = RoomController.RandomWeightedByKeyCount(m_keyPrefabs, info => info.m_keyCountMax - keyRoomCount < 0 ? int.MaxValue : info.m_keyCountMax - keyRoomCount);
		}

		if (keyRooms == null)
		{
			return; // NOTE that this is valid in the Entryway, where locks are spawned w/o keys
		}

		// spawn key(s)
		// TODO: convert any empty key rooms into bonus item rooms?
		for (int i = 0; i < keyOrLockRooms.Length && i < m_keyInfo.m_keyCountMax; ++i)
		{
			GameObject keyPrefab = m_keyInfo.m_prefabs.RandomWeighted();
			bool isItem = keyPrefab.GetComponent<Rigidbody2D>() != null;
			Vector3 spawnPos = isItem ? keyOrLockRooms[i].ItemSpawnPosition(keyPrefab) : keyOrLockRooms[i].InteriorPosition(m_keyHeightMax, keyPrefab); // TODO: prioritize placing non-items close to self if multiple in this room?
			if (isItem)
			{
				spawnPos += (Vector3)keyPrefab.OriginToCenterY();
			}
			GameObject keyObj = keyPrefab.GetComponent<ISavable>() == null ? Instantiate(keyPrefab, spawnPos, Quaternion.identity) : GameController.Instance.m_savableFactory.Instantiate(keyPrefab, spawnPos, Quaternion.identity);
			foreach (IKey key in keyObj.GetComponentsInChildren<IKey>())
			{
				key.Lock = this;
				m_keys.Add(key);
			}
		}

		// setup key(s)
		if (m_keyInfo.m_combinationDigits > 0)
		{
			// assign combination
			m_combinationSet = m_combinationSets.RandomWeighted();
			int[] combination = new int[m_keyInfo.m_combinationDigits];
			for (int digitIdx = 0; digitIdx < m_keyInfo.m_combinationDigits; ++digitIdx)
			{
				combination[digitIdx] = Random.Range(0, m_combinationSet.m_options.Length); // TODO: recognize & act upon "special" combinations (0333, 0666, real words, etc.)?
			}

			// distribute combination among keys/children
			bool useSprites = m_combinationSet.m_spriteUsagePct > 0.0f && Random.value <= m_combinationSet.m_spriteUsagePct; // NOTE the prevention of rare unexpected results when usage percent is 0 or 1
			int optionsCount = m_combinationSet.m_options.First().m_strings.Length; // TODO: don't assume all options have the same m_strings.Length?
			int optionIdxToggle = useSprites ? -1 : Random.Range(0, optionsCount);
			int optionIdxText = Random.Range(0, optionsCount); // NOTE that this can differ from optionIdxToggle even when not using sprites, in order to allow logically equivalent options within individual puzzles
			float digitsPerKey = (float)m_keyInfo.m_combinationDigits / keyOrLockRooms.Length;
			int comboIdx = 0;
			int keyIdx = 0;
			foreach (IKey key in m_keys)
			{
				if (key is InteractToggle toggle)
				{
					toggle.SetToggleText(m_combinationSet, optionIdxToggle, combination[comboIdx]);
					++comboIdx;
				}
				else
				{
					int startIdx = Mathf.RoundToInt(keyIdx * digitsPerKey);
					int endIdx = Mathf.RoundToInt((keyIdx + 1) * digitsPerKey);
					string prepend = keyIdx == 0 ? "" : "<sprite index=0>";
					IEnumerable<string> keyText = combination[startIdx .. endIdx].Select(idx => m_combinationSet.m_options[idx].m_strings[optionIdxText]);
					string append = keyIdx == keyOrLockRooms.Length - 1 ? "" : "<sprite index=0>";

					if (key is ItemController item)
					{
						item.MergeWithSourceText(prepend, keyText, append);
					}
					else
					{
						key.Component.GetComponentInChildren<TMP_Text>().text = prepend + keyText.Aggregate("", (str, strNew) => str + strNew) + append; // TODO: embed w/i (short) flavor text or use sprites?
					}
					++keyIdx;
				}
			}
		}

		// spawn order guide if applicable
		GameObject orderObj = null;
		if (m_keys.Count > 1 && m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			int spawnRoomIdx = Random.Range(0, keyOrLockRooms.Length + 1);
			RoomController spawnRoom = spawnRoomIdx >= keyOrLockRooms.Length ? lockRoom : keyOrLockRooms[spawnRoomIdx];
			GameObject orderPrefab = m_keyInfo.m_orderPrefabs.RandomWeighted();
			Vector3 spawnPos = spawnRoom.InteriorPosition(m_keyHeightMax, orderPrefab);
			orderObj = Instantiate(orderPrefab, spawnPos, Quaternion.identity, spawnRoom.transform);
		}

		// allow duplicates in ordered keys
		// TODO: re-enable after upgrading IKey.IsInPlace? allow duplicates before some originals?
		SpriteRenderer[] colorKeyRenderers = orderObj != null ? orderObj.GetComponentsInChildren<SpriteRenderer>().ToArray() : GetComponentsInChildren<SpriteRenderer>();
		//if (orderObj != null)
		//{
		//	int keyCountOrig = m_keys.Count;
		//	for (int i = 0, repeatCount = Random.Range(0, colorKeyRenderers.Length - m_keys.Count + 1); i < repeatCount; ++i)
		//	{
		//		m_keys.Add(m_keys[Random.Range(0, keyCountOrig)]);
		//	}
		//}

		// match key color(s)
		// TODO: better many-to-many logic
		int colorIdx;
		for (colorIdx = 0; colorIdx < colorKeyRenderers.Length; ++colorIdx)
		{
			SpriteRenderer rendererCur = colorKeyRenderers[colorIdx];
			if (colorIdx < m_keys.Count)
			{
				rendererCur.color = m_keys[colorIdx].Component.GetComponent<SpriteRenderer>().color;
			}
			else if (rendererCur.GetComponent<ColorRandomizer>() != null) // TODO?
			{
				rendererCur.gameObject.SetActive(false);
			}
			else if (m_keys.Count > 0)
			{
				rendererCur.color = m_keys.First().Component.GetComponent<SpriteRenderer>().color;
			}
		}
		for (; colorIdx < m_keys.Count; ++colorIdx)
		{
			m_keys[colorIdx].Component.GetComponent<SpriteRenderer>().color = colorKeyRenderers.First().color;
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
			return savableObj != null && savableKey != null && savableObj.Type != -1 && (savableObj.Type == savableKey.Type || savableObj.Type == System.Array.IndexOf(GameController.Instance.m_savableFactory.m_savables, key.Component.gameObject)); // NOTE the extra check of SavableFactory.m_savables since the key might be a pre-spawned prefab
		}

		int matchingKeyIdx = -1;
		bool retVal = false;
		if (m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			// ordered
			matchingKeyIdx = m_keys.FindIndex(key => !key.IsInPlace);
			retVal = matchingKeyIdx >= 0 && (CheckKey(m_keys[matchingKeyIdx]) || (m_keyInfo.m_genericKeys && m_keyInfo.m_prefabs.Length > matchingKeyIdx && CheckKey(m_keyInfo.m_prefabs[matchingKeyIdx].m_object.GetComponent<IKey>())));
		}
		else
		{
			// unordered
			matchingKeyIdx = m_keys.FindIndex(key => CheckKey(key));
			retVal = matchingKeyIdx >= 0 || (m_keyInfo.m_genericKeys && System.Array.Exists(m_keyInfo.m_prefabs, keyPrefab => CheckKey(keyPrefab.m_object.GetComponent<IKey>())));
		}

		// swap out generic keys if necessary
		if (retVal && m_keyInfo.m_genericKeys && matchingKeyIdx >= 0)
		{
			IKey newKey = obj.GetComponent<IKey>();
			if (m_keys[matchingKeyIdx] != newKey)
			{
				m_keys[matchingKeyIdx] = newKey;
			}
		}

		return retVal;
	}

	public bool CheckInput()
	{
		foreach (IKey key in m_keys)
		{
			if (!key.IsInPlace)
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
		m_keys.AddRange(GetComponentsInChildren<IKey>());
		foreach (IKey key in m_keys)
		{
			key.Lock = this;
		}

		m_hasTrigger = System.Array.Exists(GetComponents<Collider2D>(), collider => collider.isTrigger);
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (m_combinationSet != null)
		{
			return;
		}

		foreach (Transform tf in collider.GetComponentsInChildren<Transform>(true))
		{
			if (IsValidNextKey(tf.gameObject))
			{
				StartCoroutine(KeyUnlockDelayed(tf));
			}
		}
	}

	private void OnTriggerExit2D(Collider2D collider)
	{
		foreach (Transform tf in collider.GetComponentsInChildren<Transform>(true))
		{
			if (IsValidNextKey(tf.gameObject)) // TODO: cache?
			{
				m_unlockInProgress = false; // TODO: handle multiple keys?
				break;
			}
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

		foreach (IKey key in m_keys)
		{
			key.Deactivate();
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
		bool Unlock(IKey key)
	{
		// handle given key
		AudioSource audio = GetComponent<AudioSource>();
		if (key != null)
		{
			if (!IsValidNextKey(key.Component.gameObject)) // TODO: move to ButtonController?
			{
				// TODO: visual indication of failure?
				audio.clip = m_failureSFX.RandomWeighted();
				audio.time = 0.0f;
				audio.Play();

				foreach (IKey entry in m_keys)
				{
					entry.IsInPlace = false;
				}
				UpdateSprite();

				return false;
			}

			key.IsInPlace = true;
		}

		// check for full unlocking
		int remainingKeys = m_keys.Count(key => !key.IsInPlace);
		if (key == null || remainingKeys <= 0)
		{
			// unlock parent
			IUnlockable parentLock = Parent == null ? null : Parent.GetComponent<IUnlockable>();
			if (parentLock != null)
			{
				parentLock.Unlock(key);
			}

			// destroy/disable ourself
			if (m_destroyOnUnlock)
			{
				Simulation.Schedule<ObjectDespawn>(0.5f).m_object = gameObject;
			}
			else
			{
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
			}

			Parent = null;
			enabled = false;

			// deactivate keys
			foreach (IKey keyEntry in m_keys)
			{
				if (keyEntry == null || keyEntry.Component == null)
				{
					continue;
				}
				keyEntry.Deactivate();
			}
			m_keys.Clear();
		}

		// update sprite
		// TODO: support arbitrary key placement?
		UpdateSprite();

		// TODO: unlock animation/VFX/etc.
		audio.clip = m_unlockSFX.RandomWeighted();
		audio.time = 0.0f;
		audio.Play();

		return true;
	}

	private void UpdateSprite()
	{
		if (m_keyInfo.m_doorSprites != null && m_keyInfo.m_doorSprites.Length > 0)
		{
			GetComponent<SpriteRenderer>().sprite = m_keyInfo.m_doorSprites[^Mathf.Min(m_keyInfo.m_doorSprites.Length, m_keys.Count(key => !key.IsInPlace) + 1)];
		}
	}

	private IEnumerator KeyUnlockDelayed(Transform tf)
	{
		m_unlockInProgress = true;
		if (m_keyDelaySeconds > 0.0f)
		{
			float unlockTime = Time.time + m_keyDelaySeconds;
			yield return new WaitUntil(() => !m_unlockInProgress || Time.time >= unlockTime);
		}

		if (!m_unlockInProgress)
		{
			yield break;
		}
		m_unlockInProgress = false;

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
