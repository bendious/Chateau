using Platformer.Gameplay;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// Represebts the current vital statistics of some game entity.
	/// </summary>
	public class Health : MonoBehaviour
	{
		/// <summary>
		/// The maximum hit points for the entity.
		/// </summary>
		public int maxHP = 1;

		/// <summary>
		/// Indicates if the entity should be considered 'alive'.
		/// </summary>
		public bool IsAlive => currentHP > 0;

		int currentHP;

		/// <summary>
		/// Increment the HP of the entity.
		/// </summary>
		public void Increment()
		{
			currentHP = Mathf.Clamp(currentHP + 1, 0, maxHP);
		}

		/// <summary>
		/// Decrement the HP of the entity. Will trigger a HealthIsZero event when
		/// current HP reaches 0.
		/// </summary>
		public void Decrement()
		{
			currentHP = Mathf.Clamp(currentHP - 1, 0, maxHP);
			Animator animator = GetComponent<Animator>();
			if (animator != null)
			{
				animator.SetTrigger("hurt");
			}
			if (currentHP == 0)
			{
				HealthIsZero ev = Schedule<HealthIsZero>();
				ev.health = this;
			}
		}

		/// <summary>
		/// Decrement the HP of the entitiy until HP reaches 0.
		/// </summary>
		public void Die()
		{
			while (currentHP > 0) Decrement();
		}

		/// <summary>
		/// Increment the HP of the entitiy until HP reaches max.
		/// </summary>
		public void Respawn()
		{
			while (currentHP < maxHP)
			{
				Increment();
			}
		}


		void Awake()
		{
			currentHP = maxHP;
		}
	}
}
