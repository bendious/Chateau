using UnityEngine;


public class TableController : MonoBehaviour
{
	public Vector2 m_sizeMin = new(1.0f, 0.25f);
	public Vector2 m_sizeMax = new(4.0f, 0.5f);

	public WeightedObject<GameObject>[] m_itemPrefabs;

	public int m_itemsMin = 0;
	public int m_itemsMax = 4;


	private void Awake()
	{
		// randomize size
		float width = Random.Range(m_sizeMin.x, m_sizeMax.x);
		float height = Random.Range(m_sizeMin.y, m_sizeMax.y);
		Vector2 size = new(width, height);
		GetComponent<SpriteRenderer>().size = size;
		BoxCollider2D collider2d = GetComponent<BoxCollider2D>(); // NOTE that Unity doesn't like naming variables 'collider' w/i a MonoBehaviour...
		collider2d.size = size;
		collider2d.offset = new(0.0f, height * 0.5f);
	}

	private void Start()
	{
		// spawn items
		// TODO: more deliberate spawning
		int itemCount = Random.Range(m_itemsMin, m_itemsMax + 1);
		Vector3 size = GetComponent<Collider2D>().bounds.size;
		float extentX = size.x * 0.5f;
		for (int i = 0; i < itemCount; ++i)
		{
			GameObject itemPrefab = Utility.RandomWeighted(m_itemPrefabs);
			BoxCollider2D itemCollider = itemPrefab.GetComponent<BoxCollider2D>();
			float offsetY = size.y + itemCollider.size.y * 0.5f + itemCollider.edgeRadius;
			Vector3 spawnCenterPos = transform.position + new Vector3(Random.Range(-extentX, extentX), offsetY, 0.0f);
			Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity);
		}
	}
}
