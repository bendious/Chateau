using Platformer.Mechanics;
using Platformer.Model;
using static Platformer.Core.Simulation;


namespace Platformer.Gameplay
{
	/// <summary>
	/// This event is fired when user input should be enabled.
	/// </summary>
	public class EnablePlayerInput : Event<EnablePlayerInput>
	{
		readonly PlatformerModel model = GetModel<PlatformerModel>();

		public override void Execute()
		{
			PlayerController player = model.player;
			player.controlEnabled = true;
		}
	}

	/// <summary>
	/// Fired when the player health reaches 0. This usually would result in a
	/// PlayerDeath event.
	/// </summary>
	/// <typeparam name="HealthIsZero"></typeparam>
	public class HealthIsZero : Event<HealthIsZero>
	{
		public Health health;

		public override void Execute()
		{
			Schedule<PlayerDeath>();
		}
	}

	/// <summary>
	/// Fired when the player has died.
	/// </summary>
	/// <typeparam name="PlayerDeath"></typeparam>
	public class PlayerDeath : Event<PlayerDeath>
	{
		readonly PlatformerModel model = GetModel<PlatformerModel>();

		public override void Execute()
		{
			PlayerController player = model.player;
			if (player.health.IsAlive)
			{
				player.health.Die();
				model.virtualCamera.m_Follow = null;
				model.virtualCamera.m_LookAt = null;
				// player.collider.enabled = false;
				player.controlEnabled = false;

				if (player.audioSource && player.ouchAudio)
				{
					player.audioSource.PlayOneShot(player.ouchAudio);
				}
				player.animator.SetTrigger("hurt");
				player.animator.SetBool("dead", true);
				Schedule<PlayerSpawn>(2);
			}
		}
	}

	/// <summary>
	/// Fired when the Jump Input is deactivated by the user, cancelling the upward velocity of the jump.
	/// </summary>
	/// <typeparam name="PlayerStopJump"></typeparam>
	public class PlayerStopJump : Event<PlayerStopJump>
	{
		public PlayerController player;

		public override void Execute()
		{

		}
	}

	/// <summary>
	/// Fired when the player is spawned after dying.
	/// </summary>
	public class PlayerSpawn : Event<PlayerSpawn>
	{
		readonly PlatformerModel model = GetModel<PlatformerModel>();

		public override void Execute()
		{
			PlayerController player = model.player;
			player.collider2d.enabled = true;
			player.controlEnabled = false;
			if (player.audioSource && player.respawnAudio)
			{
				player.audioSource.PlayOneShot(player.respawnAudio);
			}
			player.health.Increment();
			player.Teleport(model.spawnPoint.transform.position);
			player.jumpState = PlayerController.JumpState.Grounded;
			player.animator.SetBool("dead", false);
			model.virtualCamera.m_Follow = player.transform;
			model.virtualCamera.m_LookAt = player.transform;
			Schedule<EnablePlayerInput>(2f);
		}
	}

	/// <summary>
	/// Fired when the player character lands after being airborne.
	/// </summary>
	/// <typeparam name="PlayerLanded"></typeparam>
	public class PlayerLanded : Event<PlayerLanded>
	{
		public PlayerController player;

		public override void Execute()
		{
		}
	}

	/// <summary>
	/// Fired when the player performs a Jump.
	/// </summary>
	/// <typeparam name="PlayerJumped"></typeparam>
	public class PlayerJumped : Event<PlayerJumped>
	{
		public PlayerController player;

		public override void Execute()
		{
			if (player.audioSource && player.jumpAudio)
			{
				player.audioSource.PlayOneShot(player.jumpAudio);
			}
		}
	}
}
