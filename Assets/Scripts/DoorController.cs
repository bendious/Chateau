using UnityEngine;


public class DoorController : MonoBehaviour
{
	[System.Serializable]
	public struct KeyInfo
	{
		public GameObject m_prefab;
		public bool m_hasRandomCombinationText;
	}
	public WeightedObject<KeyInfo>[] m_keyPrefabs;


	private GameObject m_key;


	private void Awake()
    {
		// spawn key
		Vector3 spawnPos = Camera.main.GetComponent<GameController>().RoomPosition(false, null, true); // NOTE that we don't care about locks since this occurs during the room hierarchy creation
		KeyInfo keyInfo = Utility.RandomWeighted(m_keyPrefabs);
		m_key = Instantiate(keyInfo.m_prefab, spawnPos, Quaternion.identity);

		// setup key
		if (keyInfo.m_hasRandomCombinationText)
		{
			m_key.GetComponent<ItemController>().m_overlayText = Random.Range(0, 10000).ToString("D4");
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

	// TODO: combination-based unlocking
	private void OnCollisionEnter2D(Collision2D collision)
	{
		foreach (ItemController item in collision.gameObject.GetComponentsInChildren<ItemController>())
		{
			if (item.gameObject == m_key)
			{
				if (item.transform.parent != null)
				{
					item.Detach(); // so that we can refresh inventory immediately even though object deletion is deferred
				}
				Destroy(m_key);
				Destroy(gameObject);

				// TODO: unlock SFX/VFX/etc.

				AvatarController avatar = collision.gameObject.GetComponent<AvatarController>();
				if (avatar != null)
				{
					avatar.InventorySync();
				}
			}
		}
	}


	private bool ColorsSimilar(Color a, Color b)
	{
		const float colorEpsilon = 0.2f;
		return Mathf.Abs(a.r - b.r) < colorEpsilon && Mathf.Abs(a.g - b.g) < colorEpsilon && Mathf.Abs(a.b - b.b) < colorEpsilon; // NOTE that we don't use color subtraction due to not wanting range clamping
	}
}
