using UnityEngine;


[CreateAssetMenu]
public class RoomType : ScriptableObject
{
	[System.Serializable]
	public class SpriteInfo
	{
		public Sprite m_sprite;
		public Color m_colorMin = Color.gray;
		public Color m_colorMax = Color.white;
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

	public WeightedObject<GameObject>[] m_decorationPrefabs;
	public int m_decorationsMin = 0;
	public int m_decorationsMax = 2;
	public float m_decorationHeightMin = 0.5f;
	public float m_decorationHeightMax = 2.0f;
}
