using Platformer.Gameplay;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	[RequireComponent(typeof(Collider2D))]
	public class EnemyController : AnimationController
	{
		public AudioClip ouch;

		public Transform m_target;
		public float m_moveMax = 0.1f;

		internal Collider2D _collider;
		internal AudioSource _audio;

		public Bounds Bounds => _collider.bounds;


		protected override void Awake()
		{
			base.Awake();
			_collider = GetComponent<Collider2D>();
			_audio = GetComponent<AudioSource>();
		}

		void OnCollisionEnter2D(Collision2D collision)
		{
			var player = collision.gameObject.GetComponent<PlayerController>();
			if (player != null)
			{
				var ev = Schedule<PlayerEnemyCollision>();
				ev.player = player;
				ev.enemy = this;
			}
		}

		protected override void Update()
		{
			move.x = m_target == null ? 0.0f : Mathf.Clamp(m_target.position.x - transform.position.x, -m_moveMax, m_moveMax);
			base.Update();
		}
	}
}
