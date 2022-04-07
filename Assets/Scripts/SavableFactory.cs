using UnityEngine;


[CreateAssetMenu]
public class SavableFactory : ScriptableObject
{
	// NOTE that existing entries should NOT be removed or reordered for backwards compatibility of saves!
	public GameObject[] m_savables;


	public GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
	{
		int index = System.Array.IndexOf(m_savables, prefab);
		return Instantiate(index, position, rotation).Component.gameObject;
	}

	public ISavable Instantiate(int index, Vector3 position = new Vector3(), Quaternion rotation = new Quaternion())
	{
		ISavable instance = Object.Instantiate(m_savables[index], position, rotation).GetComponent<ISavable>();
		instance.Type = index;
		return instance;
	}
}
