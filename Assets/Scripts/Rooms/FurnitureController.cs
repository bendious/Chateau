using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
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


	private Vector2 m_sizePcts;


	private void Start()
	{
		GetComponent<SpriteRenderer>().flipX = Random.value < 0.5f; // TODO: align w/ nearer wall?
	}


	public float RandomizeSize(Vector2 roomExtents)
	{
		// randomize size
		m_sizePcts = new(Random.value, Random.value);
		float width = Mathf.Lerp(m_sizeMin.x, Mathf.Min(m_sizeMax.x, roomExtents.x), m_sizePcts.x);
		float heightTotal = Mathf.Lerp(m_sizeMin.y, Mathf.Min(m_sizeMax.y, roomExtents.y), m_sizePcts.y);

		// apply size evenly across pieces
		// TODO: don't assume even stacking?
		float heightPiece = heightTotal / m_childRenderers.Length;
		Vector2 sizePiece = new(width, heightPiece);

		float heightItr = transform.localPosition.y;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
		{
			BoxCollider2D boxCollider = renderer.GetComponent<BoxCollider2D>(); // TODO: handle resizing other types of colliders?
			if (!m_childRenderers.Contains(renderer))
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

	public virtual List<GameObject> SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining, List<GameObject> prefabsSpawned, float sizeScalarX = 1.0f, float sizeScalarY = 1.0f)
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

			Vector3 spawnCenterPos = ItemSpawnPosition(itemPrefab, sizeScalarX, sizeScalarY);
			items.Add(GameController.Instance.m_savableFactory.Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity));
		}

		return items;
	}

	public virtual GameObject SpawnKey(GameObject prefab, bool isCriticalPath, float sizeScalarX = 1.0f, float sizeScalarY = 1.0f)
	{
		Vector3 spawnPos = ItemSpawnPosition(prefab, sizeScalarX, sizeScalarY);
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
		return Mathf.RoundToInt(Mathf.Lerp(Mathf.Max(m_itemsMin, roomType.m_itemsMin - minItemsOtherFurniture), Mathf.Min(m_itemsMax, roomType.m_itemsMax - minItemsOtherFurniture) + 1, Mathf.Min(1.0f, m_sizePcts.magnitude))); // TODO: correlate total item value/size w/ furniture size?
	}


	private Vector3 ItemSpawnPosition(GameObject itemPrefab, float sizeScalarX, float sizeScalarY)
	{
		// TODO: check for collision w/ existing colliders?
		Vector3 size = (m_childRenderers.Length > 0 ? m_childRenderers.First() : GetComponent<SpriteRenderer>()).size; // NOTE that physics likely hasn't updated its position, but the renderer size should be valid
		float extentX = size.x * 0.5f * sizeScalarX;
		return (Vector2)transform.position + new Vector2(Random.Range(-extentX, extentX), size.y * sizeScalarY * Random.Range(1, m_childRenderers.Length + 1)) + itemPrefab.OriginToCenterY(); // TODO: don't assume the furniture origin is at bottom center, but also don't use stale Collider2D bounds? more deliberate placement?
	}
}
