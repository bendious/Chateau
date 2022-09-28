using UnityEngine;


[CreateAssetMenu]
public class RoomType : ScriptableObject
{
	public string[] m_preconditionNames; // TODO: less error-prone type?

	[System.Serializable]
	public sealed class SpriteInfo
	{
		public Sprite m_sprite;
		public Color m_colorMin = Color.gray;
		public Color m_colorMax = Color.white;
		public bool m_proportionalColor = false;
	}
	public WeightedObject<SpriteInfo>[] m_backdrops;
	public WeightedObject<SpriteInfo>[] m_walls;

	public WeightedObject<GameObject>[] m_furniturePrefabs;
	public float m_fillPctMin = 0.25f;

	// NOTE that these weights are multiplied by FurnitureControllers'
	public WeightedObject<GameObject>[] m_itemPrefabs;
	public WeightedObject<GameObject>[] m_itemRarePrefabs;
	public int m_itemsMin = 1;
	public int m_itemsMax = 4;

	[System.Serializable] public sealed class DecorationInfo // TODO: merge w/ SpriteInfo?
	{
		public WeightedObject<GameObject>[] m_prefabs;
		public float m_rotationDegreesMax = 0.0f;
		public float m_heightMin = 0.5f;
		public float m_heightMax = 2.0f;
		public bool m_sharedHeight = true;
	}
	public WeightedObject<DecorationInfo>[] m_decorations;
	public int m_decorationsMin = 0;
	public int m_decorationsMax = 2;
}
