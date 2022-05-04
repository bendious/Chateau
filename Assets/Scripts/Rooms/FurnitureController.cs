using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class FurnitureController : MonoBehaviour
{
	public Vector2 m_sizeMin = new(1.0f, 0.25f);
	public Vector2 m_sizeMax = new(4.0f, 0.5f);

	// NOTE that these weights are multiplied by RoomTypes'
	public WeightedObject<GameObject>[] m_itemPrefabs;
	public WeightedObject<GameObject>[] m_itemRarePrefabs;

	public int m_itemsMin = 1;
	public int m_itemsMax = 4;


	public void RandomizeSize(Vector2 roomExtents)
	{
		// randomize size
		float width = Random.Range(m_sizeMin.x, Mathf.Min(m_sizeMax.x, roomExtents.x));
		float heightTotal = Random.Range(m_sizeMin.y, Mathf.Min(m_sizeMax.y, roomExtents.y));

		// apply size evenly across pieces
		// TODO: don't assume even stacking?
		Transform[] pieces = GetComponentsInChildren<Transform>();
		float heightPiece = heightTotal / pieces.Length;
		Vector2 sizePiece = new(width, heightPiece);

		float heightItr = pieces.First().localPosition.y;
		foreach (Transform piece in pieces)
		{
			piece.localPosition = new(piece.localPosition.x, heightItr, piece.localPosition.z);
			piece.GetComponent<SpriteRenderer>().size = sizePiece;
			BoxCollider2D collider2d = piece.GetComponent<BoxCollider2D>(); // NOTE that Unity doesn't like naming variables 'collider' w/i a MonoBehaviour...
			collider2d.size = sizePiece;
			collider2d.offset = new(0.0f, heightPiece * 0.5f);

			heightItr += heightPiece;
		}
	}

	public void SpawnItems(bool rare, RoomType roomType)
	{
		// determine final spawn types/weights based on furniture and room type
		int itemCount = Random.Range(Mathf.Min(m_itemsMin, roomType.m_itemsMin), Mathf.Min(m_itemsMax, roomType.m_itemsMax) + 1); // TODO: abide by RoomType min/max across multiple furnitures
		WeightedObject<GameObject>[] furnitureItems = rare ? m_itemRarePrefabs : m_itemPrefabs;
		WeightedObject<GameObject>[] roomItems = rare ? roomType.m_itemRarePrefabs : roomType.m_itemPrefabs;
		WeightedObject<GameObject>[] itemsFinal = furnitureItems.Join(roomItems, pair => pair.m_object, pair => pair.m_object, (pair1, pair2) => new WeightedObject<GameObject> { m_object = pair1.m_object, m_weight = pair1.m_weight * pair2.m_weight }).ToArray();

		if (itemsFinal.Length <= 0)
		{
			return; // NOTE that this is valid for non-bonus Entryway rooms
		}

		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		float extentX = size.x * 0.5f;
		for (int i = 0; i < itemCount; ++i)
		{
			GameObject itemPrefab = itemsFinal.RandomWeighted();
			Vector3 spawnCenterPos = (Vector2)transform.position + new Vector2(Random.Range(-extentX, extentX), size.y * Random.Range(1, transform.childCount + 2)) + Utility.OriginToCenterY(itemPrefab); // TODO: don't assume the furniture origin is at bottom center, but also don't use stale Collider2D bounds? more deliberate placement?
			GameController.Instance.m_savableFactory.Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity);
		}
	}
}
