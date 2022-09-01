using System.Linq;
using UnityEngine;


[CreateAssetMenu]
public class MaterialSystem : ScriptableObject
{
	public MaterialInfo[] m_materialInfos;

	public MaterialPairInfo[] m_materialPairInfos;


	public MaterialInfo Find(PhysicsMaterial2D material)
	{
		// TODO: duplicate prevention/detection?
		MaterialInfo backup = null;
		MaterialInfo match = m_materialInfos.FirstOrDefault(info =>
		{
			if (info.m_material == null)
			{
				Debug.Assert(backup == null);
				backup = info;
			}
			return info.m_material == material;
		});
		return match ?? backup;
	}

	public MaterialPairInfo PairBestMatch(PhysicsMaterial2D material1, PhysicsMaterial2D material2)
	{
		System.Collections.Generic.List<MaterialPairInfo> partialMatches = new();
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

			if (info.m_material1 != null && info.m_material2 != null)
			{
				continue; // fully-specified entries are too specific for partial matching
			}

			if (info.m_material1 == material1 || info.m_material2 == material2 || info.m_material1 == material2 || info.m_material2 == material1)
			{
				partialMatches.Add(info);
			}
		}

		return partialMatches.Count > 0 ? partialMatches.Random() : backupInfo; // TODO: partial match priority?
	}
}
