using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


public class LockController : MonoBehaviour, IInteractable, IUnlockable
{
	[System.Serializable]
	public struct KeyInfo
	{
		public Sprite[] m_doorSprites;
		public GameObject[] m_prefabs;
		public int m_combinationDigits;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;
	public WeightedObject<string>[] m_combinationSets;

	public GameObject m_combinationIndicatorPrefab;
	public float m_interactDistanceMax = 1.0f; // TODO: combine w/ avatar focus distance?

	public GameObject m_door;


	private readonly List<GameObject> m_keys = new();
	private KeyInfo m_keyInfo;
	private string m_combinationSet;

	private /*readonly*/ string m_combination;
	private GameObject m_indicator;
	private string m_inputCur;
	private int m_inputIdxCur = 0;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		// spawn key(s)
		KeyInfo keyInfo = Utility.RandomWeighted(m_keyPrefabs.Where(info => info.m_object.m_prefabs.Length == keyRooms.Length).ToArray());
		int i = 0;
		foreach (GameObject keyPrefab in keyInfo.m_prefabs)
		{
			Vector3 spawnPos = keyRooms[i].ChildPosition(false, null, true, false);
			m_keys.Add(Instantiate(keyPrefab, spawnPos, Quaternion.identity));
			++i;
		}

		// door setup based on key
		if (keyInfo.m_doorSprites != null && keyInfo.m_doorSprites.Length > 0)
		{
			m_keyInfo = keyInfo;
			GetComponent<SpriteRenderer>().sprite = m_keyInfo.m_doorSprites.First();
		}

		// setup key(s)
		if (keyInfo.m_combinationDigits > 0)
		{
			// assign combination
			m_combinationSet = Utility.RandomWeighted(m_combinationSets);
			m_combination = "";
			for (int digitIdx = 0; digitIdx < m_keyInfo.m_combinationDigits; ++digitIdx)
			{
				m_combination += m_combinationSet[Random.Range(0, m_combinationSet.Length)]; // TODO: recognize & act upon "special" combinations (0333, 0666, real words, etc.)?
			}
			m_inputCur = new string(Enumerable.Repeat(m_combinationSet.First(), m_keyInfo.m_combinationDigits).ToArray());

			// distribute combination among keys
			int digitsPerKey = m_keyInfo.m_combinationDigits / m_keys.Count;
			Assert.AreEqual(digitsPerKey * m_keys.Count, m_keyInfo.m_combinationDigits); // ensure digits aren't lost to truncation
			int keyIdx = 0;
			foreach (GameObject key in m_keys)
			{
				key.GetComponent<ItemController>().m_overlayText = (keyIdx == 0 ? "" : "*") + m_combination.Substring(keyIdx * digitsPerKey, digitsPerKey) + (keyIdx == m_keys.Count - 1 ? "" : "*");
				++keyIdx;
			}
		}

		// choose color
		Color color = new(Random.value, Random.value, Random.value);
		if (Utility.ColorsSimilar(color, Color.black) || Utility.ColorsSimilar(color, Color.white) || Utility.ColorsSimilar(color, RoomController.m_oneWayPlatformColor))
		{
			// avoid colors that are too close to the black/white/brown of the background/walls/platforms
			int swapIdx = Random.Range(0, 3);
			color[swapIdx] = (color[swapIdx] + 0.5f) % 1.0f;
		}

		// match color w/ key(s)
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		renderer.color = color;
		renderer.enabled = false;
		renderer.enabled = true;
		foreach (GameObject key in m_keys)
		{
			key.GetComponent<SpriteRenderer>().color = color;
		}
	}

	public void OnNavigate(GameObject overlayObj, UnityEngine.InputSystem.InputValue input)
	{
		TMPro.TMP_Text text = overlayObj.GetComponentInChildren<TMPro.TMP_Text>();

		Vector2 xyInput = input.Get<Vector2>();
		float xInput = xyInput.x;
		int xInputDir = Utility.FloatEqual(xInput, 0.0f) ? 0 : (int)Mathf.Sign(xInput);
		if (xInputDir != 0)
		{
			m_inputIdxCur = Utility.Modulo(m_inputIdxCur + xInputDir, m_keyInfo.m_combinationDigits);
			UpdateUIIndicator(text);
		}

		float yInput = xyInput.y;
		int yInputDir = Utility.FloatEqual(yInput, 0.0f) ? 0 : (int)Mathf.Sign(yInput);
		if (yInputDir != 0)
		{
			char oldChar = m_inputCur[m_inputIdxCur];
			Assert.IsTrue(m_combinationSet.Count(setChar => setChar == oldChar) == 1); // TODO: handle duplicate characters?
			int oldSetIdx = m_combinationSet.IndexOf(oldChar);
			char newDigit = m_combinationSet[Utility.Modulo(oldSetIdx + yInputDir, m_combinationSet.Length)];

			char[] inputArray = m_inputCur.ToCharArray();
			inputArray[m_inputIdxCur] = newDigit;
			m_inputCur = new string(inputArray);

			text.text = m_inputCur;
		}
	}

	public void OnSubmit(AvatarController avatar)
	{
		if (m_inputCur == m_combination)
		{
			Unlock(null);
			UICleanup(avatar);
			return;
		}

		// TODO: failure SFX?
	}

	public void UICleanup(AvatarController avatar)
	{
		avatar.Controls.SwitchCurrentActionMap("Avatar"); // NOTE that this is BEFORE clearing m_indicator since we were getting occasional OnNavigate() calls after clearing m_indicator
		Simulation.Schedule<ObjectDespawn>().m_object = m_indicator;
		m_indicator = null;
		GameObject overlayObj = avatar.m_overlayCanvas.gameObject;
		overlayObj.SetActive(false);
	}


	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!string.IsNullOrEmpty(m_combination))
		{
			return;
		}

		foreach (Transform tf in collider.GetComponentsInChildren<Transform>(true))
		{
			if (m_keys.Contains(tf.gameObject))
			{
				Unlock(tf.gameObject);
			}
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		OnTriggerEnter2D(collision.collider);
	}

	private void OnDestroy()
	{
		foreach (GameObject key in m_keys)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = key;
		}
		if (m_door != null)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_door;
		}
	}


	public /*override*/ bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_combination);

	public /*override*/ void Interact(KinematicCharacter interactor)
	{
		if (string.IsNullOrEmpty(m_combination))
		{
			return;
		}

		AvatarController avatar = (AvatarController)interactor;
		Assert.IsTrue(GameController.Instance.m_avatars.Contains(avatar));

		StopAllCoroutines();

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

	public /*override*/ void Detach(bool noAutoReplace)
	{
		Assert.IsNull("This should never be called.");
	}


#if DEBUG
	public
#else
	private
#endif
		void Unlock(GameObject key)
	{
		if (key != null)
		{
			m_keys.Remove(key);
			Simulation.Schedule<ObjectDespawn>().m_object = key;
		}
		if (key == null || m_keys.Count <= 0)
		{
			m_combination = null; // to disable interaction for consoles
			if (m_door != null)
			{
				GameController.Instance.AddCameraTarget(m_door.transform);
			}
			Simulation.Schedule<ObjectDespawn>(m_door != null ? 1.0f : 0.5f).m_object = m_door != null ? m_door : gameObject; // TODO: guarantee camera reaches m_door?
			foreach (GameObject keyRemaining in m_keys)
			{
				Simulation.Schedule<ObjectDespawn>().m_object = keyRemaining;
			}
			m_keys.Clear();
		}

		// update sprite
		// TODO: support arbitrary key placement?
		if (m_keyInfo.m_doorSprites != null && m_keys.Count < m_keyInfo.m_doorSprites.Length)
		{
			GetComponent<SpriteRenderer>().sprite = m_keyInfo.m_doorSprites[^(m_keys.Count + 1)];
		}

		// TODO: unlock animation/VFX/etc.
		GetComponent<AudioSource>().Play();
	}

	private void UpdateUIIndicator(TMPro.TMP_Text text)
	{
		m_indicator.transform.localPosition = new Vector3(Mathf.Lerp(text.textBounds.min.x, text.textBounds.max.x, (m_inputIdxCur + 0.5f) / m_keyInfo.m_combinationDigits), m_indicator.transform.localPosition.y, m_indicator.transform.localPosition.z);
	}

	private IEnumerator UpdateInteraction(AvatarController avatar)
	{
		avatar.Controls.SwitchCurrentActionMap("UI");

		GameObject overlayObj = avatar.m_overlayCanvas.gameObject;
		TMPro.TMP_Text text = overlayObj.GetComponentInChildren<TMPro.TMP_Text>();
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
