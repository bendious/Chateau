using UnityEngine;


public class Explosion : MonoBehaviour
{
	public float m_radius = 1.5f;

	public float m_damage = 1.0f;

	public float m_lifetime = 3.0f;

	public WeightedObject<AudioClip>[] m_sfx;

	public KinematicCharacter m_source;


    private void Start()
    {
		// SFX
		AudioSource audio = GetComponent<AudioSource>();
		audio.clip = Utility.RandomWeighted(m_sfx);
		audio.Play();

		// damage
		Collider2D[] objects = Physics2D.OverlapCircleAll(transform.position, m_radius, Physics2D.GetLayerCollisionMask((m_source == null ? gameObject : m_source.gameObject).layer));
		foreach (Collider2D obj in objects)
		{
			if (!obj.gameObject.activeSelf || obj.transform.parent != null) // e.g. items in backpacks
			{
				continue;
			}

			Health health = obj.GetComponent<Health>();
			if (health == null)
			{
				continue;
			}
			health.Decrement(m_source == null ? gameObject : m_source.gameObject, m_damage);
		}

		// timed despawn
		Simulation.Schedule<ObjectDespawn>(m_lifetime).m_object = gameObject;
    }

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (ConsoleCommands.ExplosionDebug)
		{
			UnityEditor.Handles.DrawWireArc(transform.position, Vector3.forward, Vector3.right, 360.0f, m_radius);
		}
	}
#endif
}
