using System.Linq;
using UnityEngine;


public class FurnitureController : MonoBehaviour
{
	public Vector2 m_sizeMin = new(1.0f, 0.25f);
	public Vector2 m_sizeMax = new(4.0f, 0.5f);

	public WeightedObject<GameObject>[] m_itemPrefabs;
	public WeightedObject<GameObject>[] m_itemRarePrefabs;

	public int m_itemsMin = 1;
	public int m_itemsMax = 4;


	private void Awake()
	{
		// randomize size
		float width = Random.Range(m_sizeMin.x, m_sizeMax.x);
		float heightTotal = Random.Range(m_sizeMin.y, m_sizeMax.y);

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


	public void SpawnItems(bool rare)
	{
		// TODO: more deliberate spawning
		int itemCount = Random.Range(m_itemsMin, m_itemsMax + 1);
		Vector3 size = GetComponent<Collider2D>().bounds.size; // NOTE that the collider likely hasn't updated its position, but the size should be valid
		float extentX = size.x * 0.5f;
		for (int i = 0; i < itemCount; ++i)
		{
			GameObject itemPrefab = Utility.RandomWeighted(rare ? m_itemRarePrefabs : m_itemPrefabs);
			BoxCollider2D itemCollider = itemPrefab.GetComponent<BoxCollider2D>();
			float offsetY = (size.y + itemCollider.edgeRadius) * 0.5f + itemCollider.size.y * Random.Range(1, transform.childCount + 2);
			Vector3 spawnCenterPos = transform.position + new Vector3(Random.Range(-extentX, extentX), offsetY, -0.1f); // NOTE the slight z offset to render items in front of higher shelves // TODO: don't assume the pivot point is at bottom center, but also don't use stale Collider2D bounds?
			Instantiate(itemPrefab, spawnCenterPos, Quaternion.identity);
		}
	}
}
