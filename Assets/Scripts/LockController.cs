using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;


[RequireComponent(typeof(Collider2D))]
public class LockController : MonoBehaviour, IInteractable, IUnlockable
{
	[System.Serializable]
	public struct KeyInfo
	{
		public Sprite[] m_doorSprites;
		public WeightedObject<GameObject>[] m_prefabs;
		public int m_keyCountMax;
		public int m_combinationDigits;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;
	public WeightedObject<string>[] m_combinationSets;

	public float m_keyHeightMax = 8.0f;

	public GameObject m_combinationIndicatorPrefab;
	public float m_interactDistanceMax = 1.0f; // TODO: combine w/ avatar focus distance?


	public GameObject Parent { get; set; }


	private readonly List<GameObject> m_keys = new();
	private KeyInfo m_keyInfo;
	private string m_combinationSet;

	private /*readonly*/ string m_combination;
	private GameObject m_indicator;
	private string m_inputCur;
	private int m_inputIdxCur = 0;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		// determine key type
		WeightedObject<KeyInfo>[] prefabCandidates = m_keyPrefabs.Where(info => info.m_object.m_keyCountMax >= keyRooms.Length).ToArray();
		if (prefabCandidates.Length <= 0)
		{
			prefabCandidates = m_keyPrefabs;
		}
		m_keyInfo = Utility.RandomWeighted(prefabCandidates);

		// spawn key(s)
		// TODO: convert any empty key rooms into bonus item rooms?
		Assert.IsTrue(m_keyInfo.m_keyCountMax > 0);
		for (int i = 0; i < keyRooms.Length && i < m_keyInfo.m_keyCountMax; ++i)
		{
			GameObject keyPrefab = Utility.RandomWeighted(m_keyInfo.m_prefabs);
			bool isItem = keyPrefab.GetComponent<Rigidbody2D>() != null;
			Vector3 spawnPos = keyRooms[i].InteriorPosition(isItem ? 0.0f : m_keyHeightMax) + (isItem ? Vector3.zero : Vector3.forward);
			GameObject keyObj = Instantiate(keyPrefab, spawnPos, Quaternion.identity);
			IUnlockable childLock = keyObj.GetComponent<IUnlockable>();
			if (childLock != null)
			{
				childLock.Parent = gameObject;
			}
			m_keys.Add(keyObj);
		}

		// door setup based on key
		if (m_keyInfo.m_doorSprites != null && m_keyInfo.m_doorSprites.Length > 0)
		{
			GetComponent<SpriteRenderer>().sprite = m_keyInfo.m_doorSprites[^Mathf.Min(m_keyInfo.m_doorSprites.Length, m_keys.Count + 1)];
		}

		// setup key(s)
		if (m_keyInfo.m_combinationDigits > 0)
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
			float digitsPerKey = (float)m_keyInfo.m_combinationDigits / m_keys.Count;
			int keyIdx = 0;
			foreach (GameObject key in m_keys)
			{
				int startIdx = Mathf.RoundToInt(keyIdx * digitsPerKey);
				int endIdx = Mathf.RoundToInt((keyIdx + 1) * digitsPerKey);
				key.GetComponentInChildren<TMP_Text>().text = (keyIdx == 0 ? "" : "*") + m_combination[startIdx..endIdx] + (keyIdx == m_keys.Count - 1 ? "" : "*");
				++keyIdx;
			}
		}

		// choose color
		Color color = new(Random.value, Random.value, Random.value);
		if (Utility.ColorsSimilar(color, Color.black) || Utility.ColorsSimilar(color, RoomController.m_oneWayPlatformColor))
		{
			// avoid colors that are too close to the black/brown of the background/platforms
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

	public bool IsKey(GameObject obj)
	{
		return m_keys.Contains(obj);
	}

	public void OnNavigate(GameObject overlayObj, UnityEngine.InputSystem.InputValue input)
	{
		TMP_Text text = overlayObj.GetComponentInChildren<TMP_Text>();

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
		if (GameController.IsReloading)
		{
			return;
		}

		foreach (GameObject key in m_keys)
		{
			DeactivateKey(key);
		}
		if (Parent != null)
		{
			Parent.GetComponent<IUnlockable>().Unlock(null);
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


#if DEBUG
	public
#else
	private
#endif
		void Unlock(GameObject key)
	{
		// handle given key
		if (key != null)
		{
			m_keys.Remove(key);
			DeactivateKey(key);
		}

		// check for full unlocking
		if (key == null || m_keys.Count <= 0)
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
			foreach (GameObject keyRemaining in m_keys)
			{
				DeactivateKey(keyRemaining);
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

	private void DeactivateKey(GameObject key)
	{
		Rigidbody2D keyBody = key.GetComponent<Rigidbody2D>();
		if (keyBody != null && keyBody.bodyType != RigidbodyType2D.Static)
		{
			// destroy item keys
			Simulation.Schedule<ObjectDespawn>().m_object = key;
			return;
		}

		// leave non-item keys in place, just turning off their light/text
		key.GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		Canvas canvas = key.GetComponentInChildren<Canvas>();
		if (canvas != null)
		{
			canvas.enabled = false;
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
