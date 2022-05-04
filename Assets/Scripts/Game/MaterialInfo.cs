using UnityEngine;


[System.Serializable]
public class MaterialInfo
{
	public PhysicsMaterial2D m_material;

	public AudioClip[] m_movementAudio;


	public AudioClip RandomMovementAudio()
	{
		return m_movementAudio[Random.Range(0, m_movementAudio.Length)];
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
		return m_collisionAudio[Random.Range(0, m_collisionAudio.Length)];
	}
}
