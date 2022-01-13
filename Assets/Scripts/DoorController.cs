using UnityEngine;


public class DoorController : MonoBehaviour
{
	public GameObject m_keyPrefab;


	private GameObject m_key;


	private void Awake()
    {
		// spawn key
		Vector3 spawnPos = Camera.main.GetComponent<GameController>().RoomPosition(false, null, true); // NOTE that we don't care about locks since this occurs during the room hierarchy creation
		m_key = Instantiate(m_keyPrefab, spawnPos, Quaternion.identity);

		// match color w/ key
		Color color = new Color(Random.value, Random.value, Random.value);
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		renderer.color = color;
		renderer.enabled = false;
		renderer.enabled = true;
		m_key.GetComponent<SpriteRenderer>().color = color;
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (collision.gameObject == m_key)
		{
			Destroy(m_key);
			Destroy(gameObject);
		}
	}
}
