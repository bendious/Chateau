using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class LockController : MonoBehaviour, IInteractable, IUnlockable
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

	public GameObject m_combinationIndicatorPrefab;
	public float m_interactDistanceMax = 1.0f; // TODO: combine w/ avatar focus distance?

	public WeightedObject<AudioClip>[] m_failureSFX;
	public WeightedObject<AudioClip>[] m_unlockSFX;


	public GameObject Parent { get; set; }


	private readonly List<IKey> m_keys = new();
	private KeyInfo m_keyInfo;
	private string m_combinationSet;

	private /*readonly*/ string m_combination;
	private GameObject m_indicator;
	private string m_inputCur;
	private int m_inputIdxCur = 0;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		if (keyRooms.Length <= 0)
		{
			return; // NOTE that this is valid in the Entryway
		}

		// determine key type
		WeightedObject<KeyInfo>[] prefabCandidates = m_keyPrefabs.Where(info => info.m_object.m_keyCountMax >= keyRooms.Length).ToArray();
		if (prefabCandidates.Length <= 0)
		{
			prefabCandidates = m_keyPrefabs;
		}
		m_keyInfo = prefabCandidates.RandomWeighted();

		// spawn key(s)
		// TODO: convert any empty key rooms into bonus item rooms?
		Assert.IsTrue(m_keyInfo.m_keyCountMax > 0);
		for (int i = 0; i < keyRooms.Length && i < m_keyInfo.m_keyCountMax; ++i)
		{
			GameObject keyPrefab = m_keyInfo.m_prefabs.RandomWeighted();
			bool isItem = keyPrefab.GetComponent<Rigidbody2D>() != null;
			Vector3 spawnPos = keyRooms[i].InteriorPosition(isItem ? 0.0f : m_keyHeightMax, isItem ? null : keyPrefab); // TODO: prefer spawning on furniture
			if (isItem)
			{
				spawnPos += (Vector3)Utility.OriginToCenterY(keyPrefab);
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
			m_inputCur = new string(Enumerable.Repeat(m_combinationSet.First(), m_keyInfo.m_combinationDigits).ToArray());

			// distribute combination among keys
			float digitsPerKey = (float)m_keyInfo.m_combinationDigits / m_keys.Count;
			int keyIdx = 0;
			foreach (IKey key in m_keys)
			{
				int startIdx = Mathf.RoundToInt(keyIdx * digitsPerKey);
				int endIdx = Mathf.RoundToInt((keyIdx + 1) * digitsPerKey);
				key.Component.GetComponentInChildren<TMP_Text>().text = (keyIdx == 0 ? "" : "*") + m_combination[startIdx..endIdx] + (keyIdx == m_keys.Count - 1 ? "" : "*");
				++keyIdx;
			}
		}

		// spawn order guide if applicable
		GameObject orderObj = null;
		if (m_keys.Count > 1 && m_keyInfo.m_orderPrefabs.Length > 0)
		{
			int spawnRoomIdx = Random.Range(0, keyRooms.Length + 1);
			RoomController spawnRoom = spawnRoomIdx >= keyRooms.Length ? lockRoom : keyRooms[spawnRoomIdx];
			GameObject orderPrefab = m_keyInfo.m_orderPrefabs.RandomWeighted();
			Vector3 spawnPos = spawnRoom.InteriorPosition(m_keyHeightMax, orderPrefab);
			orderObj = Instantiate(orderPrefab, spawnPos, Quaternion.identity, spawnRoom.transform);
		}

		// allow duplicates in ordered keys
		// TODO: re-enable after upgrading IKey.IsInPlace? allow duplicates before some originals?
		SpriteRenderer[] colorKeyRenderers = orderObj != null ? orderObj.GetComponentsInChildren<SpriteRenderer>().ToArray() : new SpriteRenderer[] { GetComponent<SpriteRenderer>() };
		//if (orderObj != null)
		//{
		//	int keyCountOrig = m_keys.Count;
		//	for (int i = 0, repeatCount = Random.Range(0, colorKeyRenderers.Length - m_keys.Count + 1); i < repeatCount; ++i)
		//	{
		//		m_keys.Add(m_keys[Random.Range(0, keyCountOrig)]);
		//	}
		//}

		// match key color(s)
		int colorIdx;
		for (colorIdx = 0; colorIdx < colorKeyRenderers.Length; ++colorIdx)
		{
			SpriteRenderer rendererCur = colorKeyRenderers[colorIdx];
			if (colorIdx < m_keys.Count)
			{
				rendererCur.color = m_keys[colorIdx].Component.GetComponent<SpriteRenderer>().color;
			}
			else
			{
				rendererCur.gameObject.SetActive(false);
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

	public void OnNavigate(GameObject overlayObj, UnityEngine.InputSystem.InputValue input)
	{
		TMP_Text text = overlayObj.GetComponentInChildren<TMP_Text>();

		Vector2 xyInput = input.Get<Vector2>();
		float xInput = xyInput.x;
		int xInputDir = xInput.FloatEqual(0.0f) ? 0 : (int)Mathf.Sign(xInput);
		if (xInputDir != 0)
		{
			m_inputIdxCur = (m_inputIdxCur + xInputDir).Modulo(m_keyInfo.m_combinationDigits);
			UpdateUIIndicator(text);
		}

		float yInput = xyInput.y;
		int yInputDir = yInput.FloatEqual(0.0f) ? 0 : (int)Mathf.Sign(yInput);
		if (yInputDir != 0)
		{
			char oldChar = m_inputCur[m_inputIdxCur];
			Assert.IsTrue(m_combinationSet.Count(setChar => setChar == oldChar) == 1); // TODO: handle duplicate characters?
			int oldSetIdx = m_combinationSet.IndexOf(oldChar);
			char newDigit = m_combinationSet[(oldSetIdx + yInputDir).Modulo(m_combinationSet.Length)];

			char[] inputArray = m_inputCur.ToCharArray();
			inputArray[m_inputIdxCur] = newDigit;
			m_inputCur = new string(inputArray);

			text.text = m_inputCur;
		}
	}

	public bool OnSubmit(AvatarController avatar)
	{
		if (m_inputCur == m_combination)
		{
			Unlock(null);
			UICleanup(avatar);
			return true;
		}

		// TODO: failure SFX?

		return false;
	}

	public void UICleanup(AvatarController avatar)
	{
		if (avatar.m_overlayCanvas.gameObject.activeSelf)
		{
			avatar.ToggleOverlay(null, null); // NOTE that this is BEFORE clearing m_indicator since we were getting occasional OnNavigate() calls after clearing m_indicator
		}
		Simulation.Schedule<ObjectDespawn>().m_object = m_indicator;
		m_indicator = null;
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


	public /*override*/ bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_combination) && !GameController.Instance.EnemiesRemain();

	public /*override*/ void Interact(KinematicCharacter interactor)
	{
		if (string.IsNullOrEmpty(m_combination))
		{
			return;
		}

		AvatarController avatar = (AvatarController)interactor;
		Assert.IsTrue(GameController.Instance.m_avatars.Contains(avatar));

		StopAllCoroutines();

		if (GameController.Instance.EnemiesRemain())
		{
			UICleanup(avatar);
			return;
		}

		bool overlayActive = avatar.ToggleOverlay(GetComponent<SpriteRenderer>(), m_inputCur);
		if (overlayActive)
		{
			StartCoroutine(UpdateInteraction(avatar));
		}
		else
		{
			UICleanup(avatar);
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
			m_combination = null; // to disable interaction for consoles

			// unlock parent or remove ourself
			IUnlockable parentLock = Parent == null ? null : Parent.GetComponent<IUnlockable>();
			if (parentLock != null)
			{
				parentLock.Unlock(key);
			}
			else
			{
				Simulation.Schedule<ObjectDespawn>(0.5f).m_object = gameObject;
			}
			Parent = null;

			// handle any non-deliverable keys
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

	private void UpdateUIIndicator(TMP_Text text)
	{
		m_indicator.transform.localPosition = new Vector3(Mathf.Lerp(text.textBounds.min.x, text.textBounds.max.x, (m_inputIdxCur + 0.5f) / m_keyInfo.m_combinationDigits), m_indicator.transform.localPosition.y, m_indicator.transform.localPosition.z);
	}

	private IEnumerator UpdateInteraction(AvatarController avatar)
	{
		avatar.Controls.SwitchCurrentActionMap("UI");

		GameObject overlayObj = avatar.m_overlayCanvas.gameObject;
		TMP_Text text = overlayObj.GetComponentInChildren<TMP_Text>();
		text.text = m_inputCur;
		yield return null; // NOTE that we have to display at least once before possibly using text.textBounds for indicator positioning

		if (m_indicator == null)
		{
			m_indicator = Instantiate(m_combinationIndicatorPrefab, overlayObj.transform);
		}
		UpdateUIIndicator(text);

		// TODO: necessary? better way of ensuring UICleanup() is invoked?
		while (overlayObj.activeSelf && Vector2.Distance(avatar.transform.position, transform.position) < m_interactDistanceMax)
		{
			yield return null;
		}
		UICleanup(avatar);
	}
}
