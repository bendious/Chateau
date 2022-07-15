using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public class FurnitureController : MonoBehaviour
{
	public Vector2 m_sizeMin = new(1.0f, 0.25f);
	public Vector2 m_sizeMax = new(4.0f, 0.5f);

	// NOTE that these weights are multiplied by RoomTypes'
	public WeightedObject<GameObject>[] m_itemPrefabs;
	public WeightedObject<GameObject>[] m_itemRarePrefabs;

	public int m_itemsMin = 1;
	public int m_itemsMax = 4;


	private void Start()
	{
		GetComponent<SpriteRenderer>().flipX = Random.value < 0.5f; // TODO: align w/ nearer wall?
	}


	public float RandomizeSize(Vector2 roomExtents)
	{
		// randomize size
		float width = Random.Range(m_sizeMin.x, Mathf.Min(m_sizeMax.x, roomExtents.x));
		float heightTotal = Random.Range(m_sizeMin.y, Mathf.Min(m_sizeMax.y, roomExtents.y));

		// apply size evenly across pieces
		// TODO: don't assume even stacking?
		SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
		float heightPiece = heightTotal / renderers.Length;
		Vector2 sizePiece = new(width, heightPiece);

		float heightItr = renderers.First().transform.localPosition.y;
		foreach (SpriteRenderer renderer in renderers)
		{
			Transform tf = renderer.transform;
			tf.localPosition = new(tf.localPosition.x, heightItr, tf.localPosition.z);
			BoxCollider2D boxCollider = tf.GetComponent<BoxCollider2D>(); // TODO: handle resizing other types of colliders?
			Vector2 colliderSizeDiff = boxCollider == null ? Vector2.zero : renderer.size - boxCollider.size;

			renderer.size = sizePiece;
			if (boxCollider != null)
			{
				// NOTE that this assumes that the top edge of the collision box is aligned w/ the top border of the sliced sprite
				boxCollider.size = sizePiece - colliderSizeDiff;
				boxCollider.offset = new(0.0f, boxCollider.size.y * 0.5f);
			}

			heightItr += heightPiece;
		}

		return width;
	}

	public int SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining)
	{
		// determine final spawn types/weights based on furniture and room type
		int minItemsOtherFurniture = itemCountExisting + furnitureRemaining * m_itemsMin; // TODO: don't assume all future furniture will have the same m_itemsMin
		int itemCount = Random.Range(Mathf.Max(m_itemsMin, roomType.m_itemsMin - minItemsOtherFurniture), Mathf.Min(m_itemsMax, roomType.m_itemsMax - minItemsOtherFurniture) + 1);
		System.Collections.Generic.IEnumerable<WeightedObject<GameObject>> itemsFinal = (rare ? m_itemRarePrefabs : m_itemPrefabs).CombineWeighted(rare ? roomType.m_itemRarePrefabs : roomType.m_itemPrefabs);

		if (itemsFinal.Count() <= 0)
		{
			return 0; // NOTE that this is valid for non-bonus Entryway rooms
		}

		System.Collections.Generic.Queue<GameObject> itemsNext = null; // TODO: share across instances of the same type in the same room?
		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		int numPlatforms = GetComponentsInChildren<SpriteRenderer>().Length; // TODO: decouple?
		float extentX = size.x * 0.5f;
		for (int i = 0; i < itemCount; ++i)
		{
			if (itemsNext == null || itemsNext.Count <= 0)
			{
				itemsNext = new(itemsFinal.RandomWeightedOrder());
			}
			GameObject itemPrefab = itemsNext.Dequeue();
			Vector3 spawnCenterPos = ItemSpawnPositionInternal(itemPrefab, extentX, size.y, numPlatforms);
			GameController.Instance.m_savableFactory.Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity);
		}

		return itemCount;
	}

	public Vector3 ItemSpawnPosition(GameObject itemPrefab)
	{
		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		return ItemSpawnPositionInternal(itemPrefab, size.x * 0.5f, size.y, GetComponentsInChildren<SpriteRenderer>().Length);
	}


	private Vector3 ItemSpawnPositionInternal(GameObject itemPrefab, float extentX, float sizeY, int numPlatforms)
	{
		// TODO: check for collision w/ existing colliders?
		return (Vector2)transform.position + new Vector2(Random.Range(-extentX, extentX), sizeY * Random.Range(1, numPlatforms + 1)) + itemPrefab.OriginToCenterY(); // TODO: don't assume the furniture origin is at bottom center, but also don't use stale Collider2D bounds? more deliberate placement?
	}
}
