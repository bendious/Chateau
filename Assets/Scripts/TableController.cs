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
		Bounds bounds = GetComponent<Collider2D>().bounds;
		for (int i = 0; i < itemCount; ++i)
		{
			GameObject itemPrefab = Utility.RandomWeighted(m_itemPrefabs);
			BoxCollider2D itemCollider = itemPrefab.GetComponent<BoxCollider2D>();
			float offsetY = bounds.size.y + itemCollider.size.y * 0.5f + itemCollider.edgeRadius;
			Vector3 spawnCenterPos = new Vector3(bounds.center.x, transform.position.y, transform.position.z) + new Vector3(Random.Range(-bounds.extents.x, bounds.extents.x), offsetY, 0.0f);
			Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity);
		}
	}
}
