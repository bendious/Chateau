using UnityEngine;


[System.Serializable]
public struct LayerMaskHelper
{
	[SerializeField] private LayerMask m_mask; // TODO: default value w/o requiring LayerMask.NameToLayer() at initialization time?


	private int m_indexCached;


	public static implicit operator int(LayerMaskHelper mask) => mask.m_mask;

	public static implicit operator LayerMask(LayerMaskHelper mask) => mask.m_mask;


	public int ToIndex()
	{
		if (m_indexCached == 0)
		{
			m_indexCached = m_mask.ToIndex();
		}
		return m_indexCached;
	}
}
