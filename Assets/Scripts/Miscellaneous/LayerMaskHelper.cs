using UnityEngine;


[System.Serializable]
public struct LayerMaskHelper
{
	[SerializeField]
	private LayerMask m_mask;   // TODO: default value w/o requiring LayerMask.NameToLayer() at initialization time?


	private int m_indexCached;


	public static implicit operator int(LayerMaskHelper mask)
	{
		if (mask.m_indexCached == 0)
		{
			mask.m_indexCached = mask.m_mask.ToIndex();
		}
		return mask.m_indexCached;
	}

	public static implicit operator LayerMask(LayerMaskHelper mask)
	{
		return mask.m_mask;
	}
}
