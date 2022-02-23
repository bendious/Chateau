using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


[CreateAssetMenu(menuName = "ScriptableObject/Material System", fileName = "MaterialSystem", order = 0)]
public class MaterialSystem : ScriptableObject
{
	public MaterialInfo[] m_materialInfos;

	public MaterialPairInfo[] m_materialPairInfos;


	public MaterialInfo Find(PhysicsMaterial2D material)
	{
		return m_materialInfos.First(info => info.m_material == material); // TODO: generic backup info? duplicate prevention?
	}

	public MaterialPairInfo PairBestMatch(PhysicsMaterial2D material1, PhysicsMaterial2D material2)
	{
		MaterialPairInfo partialMatch = null;
		MaterialPairInfo backupInfo = null; // generic backup info to ensure we always have something to use

		foreach (MaterialPairInfo info in m_materialPairInfos)
		{
			if (info.m_material1 == null && info.m_material2 == null)
			{
				Debug.Assert(backupInfo == null);
				backupInfo = info;
			}

			if ((info.m_material1 == material1 && info.m_material2 == material2) || (info.m_material1 == material2 && info.m_material2 == material1))
			{
				return info; // full match // TODO: duplicate prevention/handling?
			}

			// skip partial match logic if possible
			// TODO: prioritize between multiple partial matches?
			if (partialMatch != null || (info.m_material1 != null && info.m_material2 != null))
			{
				continue;
			}

			if (info.m_material1 == material1 || info.m_material2 == material2 || info.m_material1 == material2 || info.m_material2 == material1)
			{
				partialMatch = info;
			}
		}

		return partialMatch ?? backupInfo;
	}
}
