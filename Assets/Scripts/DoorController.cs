using System.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;


public class DoorController : MonoBehaviour, IInteractable
{
	[System.Serializable]
	public struct KeyInfo
	{
		public GameObject m_prefab;
		public bool m_hasRandomCombinationText;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;

	public int m_combinationDigits = 4;
	public float m_interactDistanceMax = 1.0f; // TODO: combine w/ avatar focus distance?


	public /*override*/ GameObject Object => gameObject;


	private GameObject m_key;

	private /*readonly*/ int m_combination = 0;
	private int m_inputCur = 0;
	private int m_inputIdxCur = 0;


	private void Awake()
    {
		// spawn key
		Vector3 spawnPos = Camera.main.GetComponent<GameController>().RoomPosition(false, null, true); // NOTE that we don't care about locks since this occurs during the room hierarchy creation
		KeyInfo keyInfo = Utility.RandomWeighted(m_keyPrefabs);
		m_key = Instantiate(keyInfo.m_prefab, spawnPos, Quaternion.identity);

		// setup key
		if (keyInfo.m_hasRandomCombinationText)
		{
			m_combination = Random.Range(1, 10000);
			m_key.GetComponent<ItemController>().m_overlayText = m_combination.ToString("D" + m_combinationDigits);
		}

		// choose color
		Color color = new(Random.value, Random.value, Random.value);
		if (ColorsSimilar(color, Color.black) || ColorsSimilar(color, Color.white) || ColorsSimilar(color, RoomController.m_oneWayPlatformColor))
		{
			// avoid colors that are too close to the black/white/brown of the background/walls/platforms
			int swapIdx = Random.Range(0, 3);
			color[swapIdx] = (color[swapIdx] + 0.5f) % 1.0f;
		}

		// match color w/ key
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		renderer.color = color;
		renderer.enabled = false;
		renderer.enabled = true;
		m_key.GetComponent<SpriteRenderer>().color = color;
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (m_combination != 0)
		{
			return;
		}

		foreach (Transform tf in collision.gameObject.GetComponentsInChildren<Transform>())
		{
			if (tf.gameObject == m_key)
			{
				Unlock();
			}
		}
	}


	public /*override*/ bool CanInteract(KinematicCharacter interactor) => m_combination != 0;

	public /*override*/ void Interact(KinematicCharacter interactor)
	{
		if (m_combination == 0)
		{
			return;
		}

		GameController gameController = Camera.main.GetComponent<GameController>();
		Assert.AreEqual(interactor, gameController.m_avatar);

		StopAllCoroutines();

		bool overlayActive = gameController.ToggleOverlay(GetComponent<SpriteRenderer>(), m_inputCur.ToString("D" + m_combinationDigits));
		if (overlayActive)
		{
			StartCoroutine(UpdateInteraction(interactor));
		}
	}


	private bool ColorsSimilar(Color a, Color b)
	{
		const float colorEpsilon = 0.2f;
		return Mathf.Abs(a.r - b.r) < colorEpsilon && Mathf.Abs(a.g - b.g) < colorEpsilon && Mathf.Abs(a.b - b.b) < colorEpsilon; // NOTE that we don't use color subtraction due to not wanting range clamping
	}

	private void Unlock()
	{
		ItemController item = m_key.GetComponent<ItemController>();
		if (item.transform.parent != null)
		{
			item.Detach(); // so that we can refresh inventory immediately even though object deletion is deferred

			AvatarController avatar = item.transform.root.GetComponent<AvatarController>();
			if (avatar != null)
			{
				avatar.InventorySync();
			}
		}
		Destroy(m_key);
		Destroy(gameObject);

		// TODO: unlock SFX/VFX/etc.
	}

	private IEnumerator UpdateInteraction(KinematicCharacter interactor)
	{
		GameObject overlayObj = Camera.main.GetComponent<GameController>().m_overlayCanvas.gameObject;

		while (Vector2.Distance(interactor.transform.position, transform.position) < m_interactDistanceMax)
		{
			// TODO: more general input parsing? visually indicate current digit
			int xInput = (Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKeyDown(KeyCode.LeftArrow) ? 1 : 0);
			m_inputIdxCur = Utility.Modulo(m_inputIdxCur + xInput, m_combinationDigits);

			int yInput = (Input.GetKeyDown(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKeyDown(KeyCode.DownArrow) ? 1 : 0);
			if (yInput != 0)
			{
				m_inputCur = Utility.Modulo(m_inputCur + yInput * (int)Mathf.Pow(10, m_combinationDigits - m_inputIdxCur - 1), 10000); // TODO: restrict to current digit

				overlayObj.GetComponentInChildren<TMPro.TMP_Text>().text = m_inputCur.ToString("D" + m_combinationDigits);
			}

			if (Input.GetKeyDown(KeyCode.Return))
			{
				if (m_inputCur == m_combination)
				{
					Unlock();
					break;
				}

				// TODO: failure SFX?
			}

			yield return null;
		}

		overlayObj.SetActive(false);
	}
}
