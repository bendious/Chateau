using UnityEngine;


[System.Serializable]
public class MaterialInfo
{
	public PhysicsMaterial2D m_material;

	[SerializeField] private AudioClip[] m_movementAudio;


	public void PlayMovementAudio(AudioSource source)
	{
		if (m_movementAudio == null || m_movementAudio.Length <= 0)
		{
			Debug.LogWarning("Material w/o movement audio?");
			return;
		}
		if (!source.enabled)
		{
			return;
		}
		source.PlayOneShot(m_movementAudio.Random());
	}
}


[System.Serializable]
public class MaterialPairInfo
{
	public PhysicsMaterial2D m_material1;
	public PhysicsMaterial2D m_material2;

	[SerializeField] private AudioClip[] m_collisionAudio;
	[SerializeField] private WeightedObject<AudioClip>[] m_collisionStrongAudio;

	// TODO: collision VFX


	public void PlayCollisionAudio(bool isStrong, AudioSource source)
	{
		bool useNormalCollisionAudio = !isStrong || m_collisionStrongAudio.Length <= 0;
		if (useNormalCollisionAudio && m_collisionAudio.Length <= 0)
		{
			Debug.LogWarning("Material w/o collision audio?");
			return;
		}
		source.PlayOneShot(useNormalCollisionAudio ? m_collisionAudio.Random() : m_collisionStrongAudio.RandomWeighted()); // TODO: separate arrays for collision/ground SFX?
	}
}
