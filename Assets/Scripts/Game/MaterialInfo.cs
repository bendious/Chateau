using UnityEngine;


[System.Serializable]
public class MaterialInfo
{
	public PhysicsMaterial2D m_material;

	public AudioClip[] m_movementAudio;
}


[System.Serializable]
public class MaterialPairInfo
{
	public PhysicsMaterial2D m_material1;
	public PhysicsMaterial2D m_material2;

	public AudioClip[] m_collisionAudio;

	// TODO: collision VFX
}
