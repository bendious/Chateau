using Platformer.Gameplay;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// This is the main class used to implement control of the avatar.
	/// </summary>
	[RequireComponent(typeof(Health))]
	public class AvatarController : AnimationController
	{
		public enum JumpState
		{
			Grounded,
			PrepareToJump,
			Jumping,
			InFlight,
			WallCling,
			Landed
		}


		public AudioClip jumpAudio;
		public AudioClip respawnAudio;

		public GameObject m_focusIndicator;
		public GameObject m_aimObject;
		public GameObject m_inventoryUI;

		public float m_aimRadius = 5.0f;
		public float m_secondaryRadiusPct = 0.5f;
		public float m_secondaryDegrees = -45.0f;

		public float m_coyoteTime = 0.15f;
		public float m_xInputForcedSmoothTime = 0.25f;

		private JumpState jumpState = JumpState.Grounded;
		private Health health;
		public bool controlEnabled = true;


		private float m_leftGroundTime = -1.0f;

		private float m_xInputForced = 0.0f;
		private float m_xInputForcedVel;

		private GameObject m_focusObj;


		protected override void Awake()
		{
			base.Awake();
			health = GetComponent<Health>();
			InventorySync();
		}

		protected override void Update()
		{
			if (controlEnabled)
			{
				if ((jumpState == JumpState.Grounded || jumpState == JumpState.WallCling || m_leftGroundTime + m_coyoteTime <= Time.time) && Input.GetButtonDown("Jump"))
				{
					jumpState = JumpState.PrepareToJump;
					if (IsWallClinging)
					{
						m_xInputForced = m_wallNormal.x;
						m_xInputForcedVel = 0.0f;
					}
				}
				else if (Input.GetButtonUp("Jump"))
				{
					stopJump = true;
				}
				move.x = Mathf.Lerp(Input.GetAxis("Horizontal"), m_xInputForced, Mathf.Abs(m_xInputForced));
				move.y = Input.GetAxis("Vertical");

				// blend x-input back from forced if necessary
				m_xInputForced = Mathf.SmoothDamp(m_xInputForced, 0.0f, ref m_xInputForcedVel, m_xInputForcedSmoothTime);

				// swing
				bool refreshInventory = false;
				if (transform.childCount > 0)
				{
					if (Input.GetButtonDown("Fire1"))
					{
						GetComponentInChildren<ItemController>().Swing();
					}

					// throw
					if (Input.GetButtonDown("Fire2"))
					{
						// TODO: show aim indicator?
					}
					else if (Input.GetButtonUp("Fire2"))
					{
						// release
						GetComponentInChildren<ItemController>().Throw();
						refreshInventory = true;
					}

					if (Input.GetButtonDown("Fire3"))
					{
						foreach (ItemController item in GetComponentsInChildren<ItemController>())
						{
							bool used = item.Use();
							if (used)
							{
								refreshInventory = true;
								break;
							}
						}
					}
				}

				// determine current focus object
				// TODO: more nuanced prioritization?
				m_focusObj = null;
				float holdRadius = ((CircleCollider2D)collider2d).radius;
				Collider2D[] focusCandidates = Physics2D.OverlapCircleAll((Vector2)transform.position + (Vector2)(Camera.main.ScreenToWorldPoint(Input.mousePosition) - transform.position).normalized * holdRadius, holdRadius * 1.5f); // TODO: restrict to certain layers?
				float distSqFocus = float.MaxValue;
				foreach (Collider2D candidate in focusCandidates)
				{
					if (ShouldIgnore(candidate.GetComponent<Rigidbody2D>(), candidate, false, false))
					{
						continue; // ignore ourself / attached/ignored objects
					}

					float distSqCur = (transform.position - candidate.transform.position).sqrMagnitude;
					if (distSqCur < distSqFocus)
					{
						distSqFocus = distSqCur;
						m_focusObj = candidate.gameObject;
					}
				}

				// place focus indicator if appropriate
				ItemController focusItem = m_focusObj == null ? null : m_focusObj.GetComponent<ItemController>();
				if (focusItem != null)
				{
					m_focusIndicator.transform.SetPositionAndRotation(m_focusObj.transform.position + Vector3.back, m_focusObj.transform.rotation); // NOTE the Z offset to ensure the focus indicator is rendered on top
					m_focusIndicator.GetComponent<SpriteRenderer>().sprite = m_focusObj.GetComponent<SpriteRenderer>().sprite;
				}
				m_focusIndicator.SetActive(focusItem != null);

				// pick up / drop items
				if (focusItem != null && Input.GetButtonDown("PickUp"))
				{
					focusItem.AttachTo(gameObject);
					m_focusObj = null;
					if (transform.childCount > m_maxPickUps)
					{
						// drop first attached to cycle through items
						transform.GetChild(0).GetComponent<ItemController>().Detach();
					}
					refreshInventory = true;
				}
				IsPickingUp = Input.GetButton("PickUp") && transform.childCount < m_maxPickUps;

				if (Input.GetButtonDown("Drop"))
				{
					if (transform.childCount > 0)
					{
						GetComponentInChildren<ItemController>().Detach();
						refreshInventory = true;
					}
				}

				// show/update inventory
				if (Input.GetButtonDown("Inventory"))
				{
					m_inventoryUI.SetActive(!m_inventoryUI.activeSelf);
					InventorySync();
				}
				else if (refreshInventory)
				{
					InventorySync();
				}
			}
			else
			{
				move.x = 0;
			}
			UpdateJumpState();
			base.Update();
		}

		protected override void FixedUpdate()
		{
			if (controlEnabled)
			{
				// aim camera/sprite
				Vector3 mousePosWS = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				m_aimObject.transform.position = transform.position + (mousePosWS - transform.position).normalized * m_aimRadius;
				m_aimDir = mousePosWS.x > transform.position.x ? 1 : -1;

				// aim items
				if (transform.childCount > 0)
				{
					// primary aim
					float holdRadius = ((CircleCollider2D)collider2d).radius;
					ItemController[] items = GetComponentsInChildren<ItemController>();
					items.First().UpdateAim(mousePosWS, holdRadius);

					// secondary hold
					Vector3 secondaryAimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, LeftFacing ? 180.0f - m_secondaryDegrees : m_secondaryDegrees) * Vector3.right;
					float secondaryRadius = m_secondaryRadiusPct * holdRadius;
					for (int i = 1; i < items.Length; ++i)
					{
						items[i].UpdateAim(secondaryAimPos, secondaryRadius);
					}
				}
			}

			base.FixedUpdate();
		}


		/// <summary>
		/// Bounce the objects velocity in a direction.
		/// </summary>
		/// <param name="dir"></param>
		public override void Bounce(Vector2 dir)
		{
			base.Bounce(dir);
			m_xInputForced = dir.x;
			m_xInputForcedVel = 0.0f;
		}

		public override void OnDeath()
		{
			base.OnDeath();
			controlEnabled = false;
			m_xInputForced = 0.0f;
			m_focusIndicator.SetActive(false);
			InventorySync();
			Schedule<GameOver>(3.0f);
		}

		protected override void DespawnSelf()
		{
			// NOTE that we purposely don't call base.DespawnSelf() since the avatar should never despawn
		}

		private static readonly Vector2 m_collisionBounceVec = new(1.0f, 2.5f);
		public void OnCollision(EnemyController enemy)
		{
			Vector2 bounceVecOriented = transform.position.x - enemy.transform.position.x < 0.0f ? new(-m_collisionBounceVec.x, m_collisionBounceVec.y) : m_collisionBounceVec;
			Bounce(bounceVecOriented);
			health.Decrement(); // NOTE that this is AFTER bouncing velocity so that OnDeath()'s reset of m_xInputForced isn't overwritten

			// temporarily disable collision to prevent getting stuck
			Collider2D enemyCollider = enemy.GetComponent<Collider2D>();
			Physics2D.IgnoreCollision(collider2d, enemyCollider, true);
			EnableCollision evt = Schedule<EnableCollision>(Health.m_invincibilityTime);
			evt.m_collider1 = collider2d;
			evt.m_collider2 = enemyCollider;
		}

		public void OnSpawn()
		{
			collider2d.enabled = true;
			body.simulated = true;
			controlEnabled = false;

			if (audioSource && respawnAudio)
			{
				audioSource.PlayOneShot(respawnAudio);
			}

			health.Respawn();

			Teleport(Vector3.zero);
			m_aimObject.transform.position = Vector3.zero;
			jumpState = JumpState.Grounded;

			animator.SetBool("dead", false);
		}

		public void InventorySync()
		{
			if (!m_inventoryUI.activeSelf)
			{
				return;
			}

			GameObject templateObj = m_inventoryUI.transform.GetChild(0).gameObject;
			Assert.IsFalse(templateObj.activeSelf);

			int iconIdx = 0;
			int iconCount = System.Math.Max(m_maxPickUps, transform.childCount);
			Vector3 posItr = templateObj.transform.position;
			for (; iconIdx < iconCount; ++iconIdx)
			{
				GameObject UIObj;
				if (iconIdx + 1 < m_inventoryUI.transform.childCount)
				{
					UIObj = m_inventoryUI.transform.GetChild(iconIdx + 1).gameObject;
				}
				else
				{
					posItr.x = templateObj.transform.position.x + (templateObj.GetComponent<RectTransform>().sizeDelta.x + templateObj.transform.position.x) * iconIdx;
					UIObj = Instantiate(templateObj, posItr, Quaternion.identity, m_inventoryUI.transform);
					UIObj.SetActive(true);
				}
				Image uiImage = UIObj.GetComponent<Image>();
				if (iconIdx < transform.childCount)
				{
					SpriteRenderer srcComp = transform.GetChild(iconIdx).GetComponent<SpriteRenderer>();
					uiImage.sprite = srcComp.sprite;
					uiImage.color = srcComp.color;
				}
				else
				{
					Image srcComp = templateObj.GetComponent<Image>();
					uiImage.sprite = srcComp.sprite;
					uiImage.color = srcComp.color;
				}
				UIObj.GetComponent<InventoryController>().m_draggable = iconIdx < transform.childCount;
			}
			for (int j = m_inventoryUI.transform.childCount - 1; j > iconCount; --j)
			{
				Destroy(m_inventoryUI.transform.GetChild(j).gameObject);
			}
		}

		public void OnVictory()
		{
			m_focusIndicator.SetActive(false);
			foreach (ItemController item in GetComponentsInChildren<ItemController>())
			{
				item.Detach();
			}
			animator.SetTrigger("victory");
			GetComponent<Health>().m_invincible = true;
			controlEnabled = false;
		}


		private void UpdateJumpState()
		{
			jump = false;
			switch (jumpState)
			{
				case JumpState.Grounded:
					if (!IsGrounded)
					{
						jumpState = JumpState.InFlight;
						m_leftGroundTime = Time.time;
					}
					break;
				case JumpState.PrepareToJump:
					jumpState = JumpState.Jumping;
					jump = true;
					stopJump = false;
					break;
				case JumpState.Jumping:
					if (!IsGrounded)
					{
						if (audioSource && jumpAudio)
						{
							audioSource.PlayOneShot(jumpAudio);
						}
						jumpState = JumpState.InFlight;
					}
					break;
				case JumpState.InFlight:
					if (IsGrounded)
					{
						jumpState = JumpState.Landed;
					}
					else if (IsWallClinging)
					{
						jumpState = JumpState.WallCling;
					}
					break;
				case JumpState.WallCling:
					if (!IsWallClinging)
					{
						jumpState = JumpState.InFlight;
						m_leftGroundTime = Time.time;
					}
					break;
				case JumpState.Landed:
					jumpState = JumpState.Grounded;
					break;
			}
		}

		// called from animation event
		private void EnablePlayerControl()
		{
			controlEnabled = true;
		}
	}
}
