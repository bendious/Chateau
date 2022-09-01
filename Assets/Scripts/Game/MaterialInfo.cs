using UnityEngine;


[System.Serializable]
public class MaterialInfo
{
	public PhysicsMaterial2D m_material;

	public AudioClip[] m_movementAudio;


	public AudioClip RandomMovementAudio()
	{
		return m_movementAudio.Random();
	}
}


[System.Serializable]
public class MaterialPairInfo
{
	public PhysicsMaterial2D m_material1;
	public PhysicsMaterial2D m_material2;

	public AudioClip[] m_collisionAudio;

	// TODO: collision VFX


	public AudioClip RandomCollisionAudio()
	{
		return m_collisionAudio.Random();
	}
}
