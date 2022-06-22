using UnityEngine;


[CreateAssetMenu]
public class SavableFactory : ScriptableObject
{
	// NOTE that existing entries should NOT be removed or reordered for backwards compatibility of saves!
	[System.Serializable] public class SavableInfo
	{
		public GameObject m_prefab;
		public int m_materialCost = 1;
	}
	public SavableInfo[] m_savables;


	public GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
	{
		int index = System.Array.FindIndex(m_savables, info => info.m_prefab == prefab);
		return Instantiate(index, position, rotation).Component.gameObject;
	}

	public ISavable Instantiate(int index, Vector3 position = new Vector3(), Quaternion rotation = new Quaternion())
	{
		ISavable instance = Object.Instantiate(m_savables[index].m_prefab, position, rotation).GetComponent<ISavable>();
		instance.Type = index;
		return instance;
	}
}
