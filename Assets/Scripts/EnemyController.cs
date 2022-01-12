﻿using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	public class EnemyController : AnimationController
	{
		public float m_targetDistance = 0.0f;
		public Transform m_target;

		public float m_meleeRange = 1.0f;


		private AIState m_aiState;


		protected override void Start()
		{
			base.Start();
			m_aiState = new AIPursue { m_target = m_target, m_targetDistance = m_targetDistance };
			m_aiState.Enter(this);
		}

		void OnCollisionEnter2D(Collision2D collision)
		{
			var avatar = collision.gameObject.GetComponent<AvatarController>();
			if (avatar != null)
			{
				avatar.OnCollision(this);
			}
		}

		protected override void Update()
		{
			if (ConsoleCommands.PassiveAI)
			{
				move = Vector2.zero;
			}
			else
			{
				AIState stateNew = m_aiState.Update(this);
				if (stateNew != null)
				{
					// TODO: split across frames?
					m_aiState.Exit(this);
					m_aiState = stateNew;
					m_aiState.Enter(this);
				}
			}

			base.Update();
		}

		protected override void FixedUpdate()
		{
			base.FixedUpdate();

			// aim items
			if (m_maxPickUps > 0 && transform.childCount > 0)
			{
				Vector2 colliderSize = ((CapsuleCollider2D)collider2d).size;
				float holdRadius = Mathf.Max(colliderSize.x, colliderSize.y) * 0.5f;
				ItemController[] items = GetComponentsInChildren<ItemController>();
				for (int i = 0; i < items.Length; ++i)
				{
					items[i].UpdateAim(m_target.position, holdRadius);
				}
			}
		}


		public override void OnDeath()
		{
			base.OnDeath();
			enabled = false;
		}

		protected override void DespawnSelf()
		{
			Camera.main.GetComponent<GameController>().OnEnemyDespawn(this);
			base.DespawnSelf();
		}


		// TODO: un-expose?
		public bool NavigateTowardTarget(Transform target, float targetDistance)
		{
			// left/right
			Vector3 targetPos = target == null ? transform.position : target.position + (transform.position - target.position).normalized * targetDistance;
			AvatarController targetAvatar = target?.GetComponent<AvatarController>();
			bool moveAway = targetAvatar != null && !targetAvatar.controlEnabled; // avoid softlock from enemies in spawn position // TODO: better shouldMoveAway flag? avoid move-away failure when current distance is less than targetDistance
			bool hasArrived = Vector2.Distance(targetPos, transform.position) < collider2d.bounds.extents.x;
			move.x = hasArrived ? 0.0f : Mathf.Clamp((targetPos.x - transform.position.x) * (moveAway ? -1.0f : 1.0f), -1.0f, 1.0f);

			// jump/drop
			// TODO: actual room-based pathfinding to avoid getting stuck
			Bounds targetBounds = targetAvatar == null ? new Bounds(targetPos, Vector3.zero) : targetAvatar.GetComponent<CircleCollider2D>().bounds;
			Bounds selfBounds = collider2d.bounds;
			if (IsGrounded && targetBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
			{
				jump = true;
			}
			move.y = targetBounds.max.y < selfBounds.min.y ? -1.0f : 0.0f;

			return hasArrived;
		}
	}
}
