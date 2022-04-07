using UnityEngine;


[CreateAssetMenu]
public class RoomType : ScriptableObject
{
	[System.Serializable]
	public struct BackdropInfo
	{
		public Sprite m_sprite;
		public float m_colorMin;
		public float m_colorMax;
	}
	public WeightedObject<BackdropInfo>[] m_backdrops;

	public WeightedObject<GameObject>[] m_furniturePrefabs;

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
