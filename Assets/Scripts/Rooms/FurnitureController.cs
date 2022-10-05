using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public class FurnitureController : MonoBehaviour
{
	[SerializeField] protected int m_itemsMin = 1;
	[SerializeField] protected int m_itemsMax = 4;


	[SerializeField] private Vector2 m_sizeMin = new(1.0f, 0.25f);
	[SerializeField] private Vector2 m_sizeMax = new(4.0f, 0.5f);

	[SerializeField] private SpriteRenderer[] m_childRenderers;

	// NOTE that these weights are multiplied by RoomTypes'
	[SerializeField] private WeightedObject<GameObject>[] m_itemPrefabs;
	[SerializeField] private WeightedObject<GameObject>[] m_itemRarePrefabs;


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
		float heightPiece = heightTotal / (m_childRenderers.Length + 1);
		Vector2 sizePiece = new(width, heightPiece);

		float heightItr = transform.localPosition.y;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
		{
			BoxCollider2D boxCollider = renderer.GetComponent<BoxCollider2D>(); // TODO: handle resizing other types of colliders?
			if (renderer.gameObject != gameObject && !m_childRenderers.Contains(renderer))
			{
				// only resize width
				// TODO: preserve ratio?
				renderer.size = new(width, renderer.size.y);
				if (boxCollider != null)
				{
					boxCollider.size = new(width, boxCollider.size.y);
				}
				continue;
			}

			Transform tf = renderer.transform;
			tf.localPosition = new(tf.localPosition.x, heightItr, tf.localPosition.z);
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

	public virtual List<GameObject> SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining, List<GameObject> prefabsSpawned)
	{
		// determine final spawn types/weights based on furniture and room type
		int itemCount = ItemCount(itemCountExisting, furnitureRemaining, roomType);
		IEnumerable<WeightedObject<GameObject>> itemsFinal = (rare ? m_itemRarePrefabs : m_itemPrefabs).CombineWeighted(rare ? roomType.m_itemRarePrefabs : roomType.m_itemPrefabs);

		List<GameObject> items = new();
		if (itemsFinal.Count() <= 0)
		{
			return items; // NOTE that this is valid for non-bonus Entryway rooms
		}

		Queue<GameObject> itemsNext = null; // TODO: share across instances of the same type in the same room?
		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		int numPlatforms = m_childRenderers.Length + 1;
		float extentX = size.x * 0.5f;
		for (int i = 0; i < itemCount; ++i)
		{
			if (itemsNext == null || itemsNext.Count <= 0)
			{
				itemsNext = new(itemsFinal.RandomWeightedOrder());
			}
			GameObject itemPrefab = itemsNext.Dequeue();

			// rare duplication prevention
			// TODO: parameterize per-type rather than by rarity?
			if (rare && prefabsSpawned.Contains(itemPrefab))
			{
				// NOTE that we don't keep trying to spawn more items due to infinite loop safety
				--i;
				--itemCount;
				continue;
			}
			prefabsSpawned.Add(itemPrefab);

			Vector3 spawnCenterPos = ItemSpawnPositionInternal(itemPrefab, extentX, size.y, numPlatforms);
			items.Add(GameController.Instance.m_savableFactory.Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity));
		}

		return items;
	}

	public virtual GameObject SpawnKey(GameObject prefab, bool isCriticalPath)
	{
		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		Vector3 spawnPos = ItemSpawnPositionInternal(prefab, size.x * 0.5f, size.y, (m_childRenderers.Length + 1));
		GameObject keyObj = prefab.GetComponent<ISavable>() == null ? Instantiate(prefab, spawnPos, Quaternion.identity) : GameController.Instance.m_savableFactory.Instantiate(prefab, spawnPos, Quaternion.identity);

		ItemController item = keyObj.GetComponent<ItemController>();
		if (item != null)
		{
			item.IsCriticalPath = isCriticalPath;
		}

		return keyObj;
	}


	protected int ItemCount(int itemCountExisting, int furnitureRemaining, RoomType roomType)
	{
		int minItemsOtherFurniture = itemCountExisting + furnitureRemaining * m_itemsMin; // TODO: don't assume all future furniture will have the same m_itemsMin
		return Random.Range(Mathf.Max(m_itemsMin, roomType.m_itemsMin - minItemsOtherFurniture), Mathf.Min(m_itemsMax, roomType.m_itemsMax - minItemsOtherFurniture) + 1); // TODO: correlate w/ size?
	}


	private Vector3 ItemSpawnPositionInternal(GameObject itemPrefab, float extentX, float sizeY, int numPlatforms)
	{
		// TODO: check for collision w/ existing colliders?
		return (Vector2)transform.position + new Vector2(Random.Range(-extentX, extentX), sizeY * Random.Range(1, numPlatforms + 1)) + itemPrefab.OriginToCenterY(); // TODO: don't assume the furniture origin is at bottom center, but also don't use stale Collider2D bounds? more deliberate placement?
	}
}
