using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


public class DoorController : MonoBehaviour, IInteractable
{
	[System.Serializable]
	public struct KeyInfo
	{
		public Sprite[] m_doorSprites;
		public GameObject[] m_prefabs;
		public bool m_hasRandomCombinationText;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;

	public GameObject m_combinationIndicatorPrefab;
	public int m_combinationDigits = 4; // TODO: vary?
	public float m_interactDistanceMax = 1.0f; // TODO: combine w/ avatar focus distance?


	private readonly List<GameObject> m_keys = new();
	private Sprite[] m_doorSprites;

	private /*readonly*/ int m_combination = 0;
	private GameObject m_indicator;
	private int m_inputCur = 0;
	private int m_inputIdxCur = 0;


	public void SpawnKeys(RoomController room)
	{
		// spawn key(s)
		KeyInfo keyInfo = Utility.RandomWeighted(m_keyPrefabs);
		foreach (GameObject keyPrefab in keyInfo.m_prefabs)
		{
			Vector3 spawnPos = room.ChildPosition(false, null, true, false); // NOTE that we don't care about locks since this occurs during the room hierarchy creation
			m_keys.Add(Instantiate(keyPrefab, spawnPos, Quaternion.identity));
		}

		// door setup based on key
		if (keyInfo.m_doorSprites != null && keyInfo.m_doorSprites.Length > 0)
		{
			m_doorSprites = keyInfo.m_doorSprites;
			GetComponent<SpriteRenderer>().sprite = m_doorSprites.First();
		}

		// setup key(s)
		if (keyInfo.m_hasRandomCombinationText)
		{
			m_combination = Random.Range(1, Mathf.RoundToInt(Mathf.Pow(10, m_combinationDigits))); // TODO: recognize & act upon "special" combinations (0333, 0666, etc.)?
			int digitsPerKey = m_combinationDigits / m_keys.Count;
			Assert.AreEqual(digitsPerKey * m_keys.Count, m_combinationDigits); // ensure digits aren't lost to truncation
			string comboStr = m_combination.ToString("D" + m_combinationDigits);
			int i = 0;
			foreach (GameObject key in m_keys)
			{
				key.GetComponent<ItemController>().m_overlayText = (i == 0 ? "" : "*") + comboStr.Substring(i * digitsPerKey, digitsPerKey) + (i == m_keys.Count - 1 ? "" : "*");
				++i;
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

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (m_combination != 0)
		{
			return;
		}

		foreach (Transform tf in collision.gameObject.GetComponentsInChildren<Transform>(true))
		{
			if (m_keys.Contains(tf.gameObject))
			{
				Unlock(tf.gameObject);
			}
		}
	}

	private void OnDestroy()
	{
		foreach (GameObject key in m_keys)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = key;
		}
	}


	public /*override*/ bool CanInteract(KinematicCharacter interactor) => m_combination != 0;

	public /*override*/ void Interact(KinematicCharacter interactor)
	{
		if (m_combination == 0)
		{
			return;
		}

		AvatarController avatar = (AvatarController)interactor;
		Assert.IsTrue(GameController.Instance.m_avatars.Contains(avatar));

		// NOTE that we can't use Stop{All}Coroutine{s}() since UpdateInteraction() has to do cleanup; we rely on it detecting overlay toggling even from other sources

		bool overlayActive = avatar.ToggleOverlay(GetComponent<SpriteRenderer>(), m_inputCur.ToString("D" + m_combinationDigits));
		if (overlayActive)
		{
			StartCoroutine(UpdateInteraction(interactor));
		}
	}

	public /*override*/ void Detach(bool noAutoReplace)
	{
		Assert.IsNull("This should never be called.");
	}


	private void Unlock(GameObject key)
	{
		if (key != null)
		{
			m_keys.Remove(key);
			Simulation.Schedule<ObjectDespawn>().m_object = key;
		}
		if (key == null || m_keys.Count <= 0)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
		}

		// update sprite
		// TODO: support arbitrary key placement?
		if (m_doorSprites != null && m_keys.Count > 0 && m_keys.Count <= m_doorSprites.Length)
		{
			GetComponent<SpriteRenderer>().sprite = m_doorSprites[^m_keys.Count];
		}

		// TODO: unlock SFX/VFX/etc.
	}

	private IEnumerator UpdateInteraction(KinematicCharacter interactor)
	{
		AvatarController avatar = (AvatarController)interactor;
		GameObject overlayObj = avatar.m_overlayCanvas.gameObject;
		TMPro.TMP_Text text = overlayObj.GetComponentInChildren<TMPro.TMP_Text>();
		text.text = m_inputCur.ToString("D" + m_combinationDigits);
		yield return null; // NOTE that we have to display at least once before possibly using text.textBounds for indicator positioning

		if (m_indicator == null)
		{
			m_indicator = Instantiate(m_combinationIndicatorPrefab, overlayObj.transform);
		}

		bool firstUpdate = true;
		while (overlayObj.activeSelf && Vector2.Distance(interactor.transform.position, transform.position) < m_interactDistanceMax)
		{
			PlayerControls.UIActions controls = avatar.Controls.UI;

			if (firstUpdate || controls.Navigate.triggered) // TODO: differentiate between co-op players
			{
				Vector2 xyInput = controls.Navigate.ReadValue<Vector2>();
				float xInput = xyInput.x;
				int xInputDir = Utility.FloatEqual(xInput, 0.0f) ? 0 : (int)Mathf.Sign(xInput);
				if (firstUpdate || xInputDir != 0)
				{
					m_inputIdxCur = Utility.Modulo(m_inputIdxCur + xInputDir, m_combinationDigits);
					m_indicator.transform.localPosition = new Vector3(Mathf.Lerp(text.textBounds.min.x, text.textBounds.max.x, (m_inputIdxCur + 0.5f) / m_combinationDigits), m_indicator.transform.localPosition.y, m_indicator.transform.localPosition.z);
				}

				float yInput = xyInput.y;
				int yInputDir = Utility.FloatEqual(yInput, 0.0f) ? 0 : (int)Mathf.Sign(yInput);
				if (yInputDir != 0)
				{
					int digitScalar = (int)Mathf.Pow(10, m_combinationDigits - m_inputIdxCur - 1);
					int oldDigit = m_inputCur / digitScalar % 10;
					int newDigit = Utility.Modulo(oldDigit + yInputDir, 10);
					m_inputCur += (newDigit - oldDigit) * digitScalar;

					text.text = m_inputCur.ToString("D" + m_combinationDigits);
				}
			}

			if (controls.Submit.triggered)
			{
				if (m_inputCur == m_combination)
				{
					Unlock(null);
					break;
				}

				// TODO: failure SFX?
			}

			firstUpdate = false;

			yield return null;
		}

		Simulation.Schedule<ObjectDespawn>().m_object = m_indicator;
		m_indicator = null;
		overlayObj.SetActive(false);
	}
}
