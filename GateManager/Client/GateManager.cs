/*
 * 
 * Gate Manager
 * Author: Timothy Dexter
 * Release: 0.0.1
 * Date: 2/10/18
 * 
 * 
 * Known Issues
 * 1) I was told the gate dimensions on Hayes Auto gate are the only incorrect ones
 *    Must initialize w/ hard coded length if you add
 * 
 * Please send any edits/improvements/bugs to this script back to the author. 
 * 
 * Usage 
 * - Add a gate location, multiplier must be 1 or -1 depending on direction it slides open
 *   duty, heading, and access range = distance in meters squared
 * - MaxTravelDistance is determined calculating length between the gates open and close positions
 * 
 * History:
 * Revision 0.0.1 2018/02/10 12:15:06 EDT TimothyDexter 
 * - Initial release
 * 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using CitizenFX.Core.UI;
using Custom.Client.Classes.Player;
using Custom.Client.Helpers;
using Custom.SharedClasses;
using Newtonsoft.Json;

namespace Custom.Client.Classes.Environment
{
	internal class GateManager
	{
		private static Entity _gateEntity;
		private static bool _gatePositionSet;
		private static Vector3 _gateTarget = Vector3.One;
		private static bool _initializedLocks;

		private static int _vehicleBlockingGate;
		private static DateTime _lastVehicleCheckTime;

		private static Vector3 _vehicleCheckPos;

		private static GateModel _currentGate;
		private static int _currentGateId;

		public static readonly Dictionary<int, GateModel> LockableGates = new Dictionary<int, GateModel> {
			{
				0,
				new GateModel( "hei_prop_station_gate", new Vector3( 488.895f, -1017.210f, 27.147f ), GateMovement.LeftToRight,
					new[] {"Police.MissionRow"}, 90f, 150f, 5.4f ) //Mission Row Gate
			}, {
				1,
				new GateModel( "prop_gate_airport_01", new Vector3(1817.867f, 3251.228f, 42.487f), GateMovement.LeftToRight,
					new [] { "SSAirfield.Gates", "Emergency.General" }, 249.186f, 100f, 7f )
			}, {
				2,
				new GateModel( "prop_gate_airport_01", new Vector3(1796.939f, 3313.328f, 40.925f), GateMovement.LeftToRight,
					new [] { "SSAirfield.Gates", "Emergency.General" }, 299.949f, 100f, 7f )
			}
		};

		public static void Init() {
			try {
				Client.ActiveInstance.RegisterTickHandler( GateControlTick );
				Client.ActiveInstance.RegisterTickHandler( GateMovementTick );

				Client.ActiveInstance.RegisterEventHandler( "GateManager.InitializeGateLocks",
					new Action<string>( HandleInitializeGateLocks ) );

				Client.ActiveInstance.RegisterEventHandler( "GateManager.ToggleLock",
					new Action<int, bool>( HandleToggleLock ) );

				Client.ActiveInstance.RegisterEventHandler( "GateManager.ToggleBreach",
					new Action<int>( HandleToggleBreach ) );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Gate Control tick
		/// </summary>
		private static async Task GateControlTick() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 5000 );
					return;
				}
				//Initialize gate locks
				if( !_initializedLocks ) {
					BaseScript.TriggerServerEvent( "Gate.InitializeLocks" );
					_initializedLocks = true;
				}

				var playerPos = Cache.PlayerPos;
				//Active nearby gate
				if( _gateEntity != null ) {
					if( playerPos.DistanceToSquared2D( _gateEntity.Position ) > 800f ) {
						_gateEntity = null;
						_gateTarget = Vector3.One;
						return;
					}

					if( !PlayerHasPermission( _currentGate ) && !_currentGate.IsBreached ) return;

					var gateControlOffsetPosition =
						new Vector3( _gateEntity.Position.X - 4f, _gateEntity.Position.Y - 1f, _gateEntity.Position.Z );
					if( _gateEntity == null ||
					    !(playerPos.DistanceToSquared2D( gateControlOffsetPosition ) < _currentGate.AccessRange) ) return;

					Screen.DisplayHelpTextThisFrame(
						$"Press ~INPUT_THROW_GRENADE~ to {(_currentGate.IsLocked ? "~r~Unlock" : "~g~Lock")} Gate" );
					if( !Game.IsControlPressed( 0, Control.ThrowGrenade ) ) return;

					ToggleGateLock();
				}
				else {
					SearchForNearbyGates( playerPos );
				}

				await BaseScript.Delay( 1500 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toggle gate lock
		/// </summary>
		private static void ToggleGateLock() {
			try {
				if( _currentGate == null ) return;

				_gateTarget = _currentGate.IsLocked
					? GetGateOffsetPosition( _currentGate, _currentGate.ClosedPosition, _currentGate.GateLength,
						_currentGate.OpeningMovement )
					: _currentGate.ClosedPosition;
				_currentGate.IsLocked = !_currentGate.IsLocked;

				_vehicleCheckPos = _currentGate.ClosedPosition;
				_vehicleBlockingGate = VehicleInteraction.GetClosetVehicleAtPosition( _vehicleCheckPos, 100f );
				BaseScript.TriggerServerEvent( "Gate.Toggle", _currentGateId, _currentGate.IsLocked );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Search for nearby gates, update them if they're close, set current game variables if player within access range
		/// </summary>
		/// <param name="playerPos"></param>
		private static void SearchForNearbyGates( Vector3 playerPos ) {
			try {

				var props = new ObjectList();
				foreach( var handle in props ) {
					var entity = Entity.FromHandle( handle );
					if( entity == null || !entity.Exists() ) continue;

					var entityHash = entity.Model.Hash;
					foreach( var gate in LockableGates ) {

						if( entityHash != gate.Value.Hash || playerPos.DistanceToSquared2D( gate.Value.ClosedPosition ) > 3000f ) continue;

						var propList = Props.FindProps( gate.Value.ModelName, 3000f );
						var gateProp = propList.OrderBy( p => Entity.FromHandle( p )?.Position.DistanceToSquared2D( playerPos ) )
							.FirstOrDefault();

						entity = Entity.FromHandle( gateProp );
						if( entity == null || !entity.Exists() ) continue;

						if( playerPos.DistanceToSquared2D( gate.Value.ClosedPosition ) > 800f ) {
							if( DateTime.Now.CompareTo( gate.Value.LastUpdate ) < 0 ) continue;

							gate.Value.LastUpdate = DateTime.Now.AddSeconds( 5 );
							SetGatePosition( entity, gate.Value );
							continue;
						}

						_gateEntity = entity;
						_currentGate = gate.Value;
						_currentGateId = gate.Key;
						var offSetPosition = GetGateOffsetPosition( _currentGate, _currentGate.ClosedPosition, _currentGate.GateLength,
							_currentGate.OpeningMovement );
						_gateTarget = _currentGate.IsLocked ? _currentGate.ClosedPosition : offSetPosition;
						_gateEntity.Position = _gateTarget;

						break;
					}
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Control gate movement tick
		/// </summary>
		private static async Task GateMovementTick() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 5000 );
					return;
				}

				if( _gateEntity == null ) {
					_currentGate = null;
					_gatePositionSet = false;

					await BaseScript.Delay( 1000 );
					return;
				}

				if( !_gatePositionSet ) {
					SetGatePosition( _gateEntity, _currentGate );
					_gatePositionSet = true;
				}

				var isGateAtTargetPostiion = _gateEntity.Position == _gateTarget;
				if( isGateAtTargetPostiion ) {
					await BaseScript.Delay( 500 );
					return;
				}

				ClampGatePosition();

				if( _gateEntity.Position.DistanceToSquared2D( _gateTarget ) >= 0.01 ) {
					await MoveGateTowardsTarget();
					PeriodicVehicleCheck();
				}
				else {
					_gateEntity.Position = _gateTarget;
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Periodically check for vehicles
		/// </summary>
		private static void PeriodicVehicleCheck() {
			try {
				if( DateTime.Now.CompareTo( _lastVehicleCheckTime ) < 0 ) return;

				_lastVehicleCheckTime = DateTime.Now.AddMilliseconds( 250 );
				_vehicleBlockingGate = VehicleInteraction.GetClosetVehicleAtPosition( _vehicleCheckPos, 100f );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Return gate offset position when moving
		/// </summary>
		/// <param name="currentGate"></param>
		/// <param name="currentPosition"></param>
		/// <param name="distance">distance from current position</param>
		/// <param name="openingMotion">direction from current position</param>
		private static Vector3 GetGateOffsetPosition( GateModel currentGate, Vector3 currentPosition, float distance,
			GateMovement openingMotion ) {
			try {
				if( currentGate == null ) return Vector3.One;

				var heading = currentGate.Heading + 180f;

				var cosx = Math.Cos( heading * (Math.PI / 180f) );
				var siny = Math.Sin( heading * (Math.PI / 180f) );

				var deltax = distance * cosx * (int)openingMotion;
				var deltay = distance * siny * (int)openingMotion;

				var newX = (float)(currentPosition.X + deltax);
				var newY = (float)(currentPosition.Y + deltay);

				return new Vector3( newX, newY, currentPosition.Z );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}

			return Vector3.One;
		}

		/// <summary>
		///     Move the gate towards its current client
		/// </summary>
		private static async Task MoveGateTowardsTarget() {
			try {
				var openingMotion = _currentGate.OpeningMovement; ;
				if( _currentGate.IsLocked ) {
					openingMotion = _currentGate.OpeningMovement == GateMovement.LeftToRight ? GateMovement.RightToLeft : GateMovement.LeftToRight;
				}

				const float stepSize = 0.015f;
				var newPosition = GetGateOffsetPosition( _currentGate, _gateEntity.Position, stepSize, openingMotion );
				if( newPosition == Vector3.One ) return;

				_gateEntity.Position = newPosition;

				var obstruction = Entity.FromHandle( _vehicleBlockingGate );
				if( obstruction != null && obstruction.Exists() ) {
					var currentTarget = _gateTarget;
					while( HasFenceCollided( obstruction ) ) {
						await BaseScript.Delay( 100 );

						if( _gateEntity == null || currentTarget != _gateTarget ) break;
					}
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Return whether or not the fence has collided with entity
		/// </summary>
		/// <param name="obstruction">vehicle obstructing fence</param>
		private static bool HasFenceCollided( Entity obstruction ) {
			try {
				if( _gateEntity == null || obstruction == null ) return false;

				var fenceCollide = Function.Call<bool>( Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, _gateEntity.Handle );
				var fenceTouch = _gateEntity.IsTouching( obstruction );

				return fenceCollide || fenceTouch;
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return false;
			}
		}

		/// <summary>
		///     Clamp the gate position so it doesn't go too far
		/// </summary>
		private static void ClampGatePosition() {
			try {
				if( _currentGate == null || _gateEntity == null ) return;
				var maxDistance = Math.Pow( _currentGate.GateLength, 2 );
				if( _gateEntity.Position.DistanceToSquared2D( _gateTarget ) > maxDistance + 0.5f )
					_gateEntity.Position = _gateTarget;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Set the gate position, freeze it, and record collisions
		/// </summary>
		/// <param name="gateEntity"></param>
		/// <param name="currGate">current gate model</param>
		private static void SetGatePosition( Entity gateEntity, GateModel currGate ) {
			try {
				if( gateEntity == null ) return;
				if( currGate != null )
					if( !currGate.IsLocked )
						gateEntity.Position = GetGateOffsetPosition( currGate, currGate.ClosedPosition, currGate.GateLength,
							currGate.OpeningMovement );
				gateEntity.IsPositionFrozen = true;
				gateEntity.IsRecordingCollisions = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Return whether or not the player has duty access for the gate
		/// </summary>
		/// <param name="gate"></param>
		private static bool PlayerHasPermission( GateModel gate ) {
			try {
				foreach( var permission in gate.Permissions ) {
					if( Business.Business.HasPermission( permission ) ) return true;
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}

			return false;
		}

		/// <summary>
		///     Handle server event to breach lock
		/// </summary>
		/// <param name="id">gate key</param>
		private static void HandleToggleBreach( int id ) {
			try {
				if( !LockableGates.TryGetValue( id, out _ ) ) return;

				LockableGates[id].IsBreached = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle server event to update lock status
		/// </summary>
		/// <param name="id">gate key</param>
		/// <param name="isLocked"></param>
		private static void HandleToggleLock( int id, bool isLocked ) {
			try {
				if( !LockableGates.TryGetValue( id, out var updatedGate ) ) return;

				LockableGates[id].IsLocked = isLocked;
				//Need refresh if entity is visble and foreign client toggles lock. 
				if( _gateEntity == null || _currentGate != updatedGate ) return;
				//Updated gate is in front of us
				var updatedPositionTarget = _currentGate.IsLocked
					? _currentGate.ClosedPosition : GetGateOffsetPosition( _currentGate, _currentGate.ClosedPosition, _currentGate.GateLength,
						_currentGate.OpeningMovement );
				//Check if client is the one that unlocked
				if( _gateTarget == updatedPositionTarget ) {
					return;
				}

				_gateTarget = updatedPositionTarget;
				_vehicleCheckPos = _currentGate.ClosedPosition;
				if( _currentGate != null )
					_vehicleBlockingGate = VehicleInteraction.GetClosetVehicleAtPosition( _vehicleCheckPos, 100f );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle server event to initialize lock status
		/// </summary>
		/// <param name="data">lock data</param>
		private static void HandleInitializeGateLocks( string data ) {
			try {
				var dict = JsonConvert.DeserializeObject<Dictionary<int, bool>>( data );
				foreach( var kvp in dict )
					if( LockableGates.TryGetValue( kvp.Key, out _ ) )
						LockableGates[kvp.Key].IsLocked = kvp.Value;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		public enum GateMovement
		{
			RightToLeft = 1,
			LeftToRight = -1
		}

		public class GateModel
		{
			public readonly float AccessRange;
			public readonly Vector3 ClosedPosition;
			public readonly string[] Permissions;
			public readonly float Heading;
			public readonly string ModelName;
			public readonly int Hash;
			public float GateLength;
			public bool IsBreached;
			public bool IsLocked;
			public DateTime LastUpdate;
			public GateMovement OpeningMovement;

			public GateModel( string entityModel, Vector3 closedPosition, GateMovement openingMotion, string[] permissions,
				float heading = -1f,
				float accessRange = 1f, float maxTravelDistance = -1, bool isLocked = true ) {
				ModelName = entityModel;
				Hash = Game.GenerateHash( ModelName );
				ClosedPosition = closedPosition;
				OpeningMovement = openingMotion;
				Heading = heading;
				AccessRange = accessRange;
				Permissions = permissions;
				IsLocked = isLocked;
				IsBreached = false;

				if( maxTravelDistance < 0 ) {
					var minDimensions = Vector3.Zero;
					var maxDimensions = Vector3.Zero;

					API.GetModelDimensions( (uint)API.GetHashKey( ModelName ), ref minDimensions, ref maxDimensions );
					GateLength = maxDimensions.X < 2 ? 5.75f : maxDimensions.X;
				}
				else {
					GateLength = maxTravelDistance;
				}
			}
		}
	}
}