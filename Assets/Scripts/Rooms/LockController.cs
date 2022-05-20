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
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;
	public WeightedObject<string>[] m_combinationSets;

	public float m_keyHeightMax = 7.5f;

	public WeightedObject<AudioClip>[] m_failureSFX;
	public WeightedObject<AudioClip>[] m_unlockSFX;


	[SerializeField]
	private bool m_destroyOnUnlock = true;


	public GameObject Parent { get; set; }


	private readonly List<IKey> m_keys = new();
	private KeyInfo m_keyInfo;
	private string m_combinationSet;

	private /*readonly*/ string m_combination;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		if (keyRooms == null)
		{
			return; // NOTE that this is valid in the Entryway, where locks are spawned w/o keys
		}

		// determine key type
		RoomController[] keyOrLockRooms = keyRooms.Length > 0 ? keyRooms : new RoomController[] { lockRoom };
		if (m_keyPrefabs.Length > 0)
		{
			m_keyInfo = RoomController.RandomWeightedByKeyCount(m_keyPrefabs, info => info.m_keyCountMax - keyRooms.Length < 0 ? int.MaxValue : info.m_keyCountMax - keyRooms.Length);
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
			m_combination = "";
			for (int digitIdx = 0; digitIdx < m_keyInfo.m_combinationDigits; ++digitIdx)
			{
				m_combination += m_combinationSet[Random.Range(0, m_combinationSet.Length)]; // TODO: recognize & act upon "special" combinations (0333, 0666, real words, etc.)?
			}

			// distribute combination among keys/children
			float digitsPerKey = (float)m_keyInfo.m_combinationDigits / keyOrLockRooms.Length;
			int comboIdx = 0;
			int keyIdx = 0;
			foreach (IKey key in m_keys)
			{
				if (key is InteractToggle toggle)
				{
					toggle.SetToggleText(m_combinationSet, m_combination[comboIdx].ToString());
					++comboIdx;
				}
				else
				{
					int startIdx = Mathf.RoundToInt(keyIdx * digitsPerKey);
					int endIdx = Mathf.RoundToInt((keyIdx + 1) * digitsPerKey);
					key.Component.GetComponentInChildren<TMP_Text>().text = (keyIdx == 0 ? "" : "*") + m_combination[startIdx .. endIdx] + (keyIdx == keyOrLockRooms.Length - 1 ? "" : "*");
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
		return m_keyInfo.m_orderPrefabs != null && m_keyInfo.m_orderPrefabs.Length > 0 ? m_keys.Count > 0 && m_keys.First(key => !key.IsInPlace).Component.gameObject == obj : m_keys.Exists(key => key.Component.gameObject == obj);
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
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!string.IsNullOrEmpty(m_combination))
		{
			return;
		}

		foreach (Transform tf in collider.GetComponentsInChildren<Transform>(true))
		{
			if (IsValidNextKey(tf.gameObject))
			{
				IKey key = tf.GetComponent<IKey>();
				key.Use();
				Unlock(key);
			}
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		OnTriggerEnter2D(collision.collider);
	}

	private void OnDestroy()
	{
		if (GameController.IsReloading)
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
				Collider2D collider = GetComponent<Collider2D>();
				if (collider != null)
				{
					collider.enabled = false;
				}
				VisualEffect vfx = GetComponent<VisualEffect>();
				if (vfx != null)
				{
					vfx.Stop();
				}
				Light2D light = GetComponent<Light2D>();
				if (light != null)
				{
					light.enabled = false;
				}
			}

			Parent = null;

			// deactivate keys
			foreach (IKey keyEntry in m_keys)
			{
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
}
