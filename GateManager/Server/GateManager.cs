using System;
using System.Collections.Generic;
using System.Linq;
using CitizenFX.Core;
using Custom.Enums.Character;
using Custom.Server.Classes.Jobs;
using Custom.Server.ClassesProcessing;
using Custom.SharedClasses;
using Newtonsoft.Json;

namespace Custom.Server.Classes.Environment
{
	internal static class Lockables
	{
		private static readonly Dictionary<int, bool> GateStates = new Dictionary<int, bool>();

		public static void Init() {
			Server.ActiveInstance.RegisterEventHandler( "Gate.Toggle", new Action<Player, int, bool>( HandleGateToggle ) );
			Server.ActiveInstance.RegisterEventHandler( "Gate.Breach", new Action<Player, int>( HandleGateBreach ) );
			Server.ActiveInstance.RegisterEventHandler( "Gate.InitializeLocks", new Action<Player>( InitializeGateLocks ) );
		}

		private static void HandleGateToggle( [FromSource] Player source, int id, bool state ) {
			try {
				var character = SessionManager.SessionList[source.Handle].Character;
				var nameStr = string.Concat( character.FirstName, ",", character.LastName );
				Log.Verbose(
					$"{nameStr} toggled gateLock[{id}] to isLocked={state}" );
				GateStates[id] = state;
				BaseScript.TriggerClientEvent( "GateManager.ToggleLock", id, state );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		private static void HandleGateBreach( [FromSource] Player source, int id ) {
			try {
				var character = SessionManager.SessionList[source.Handle].Character;
				var nameStr = string.Concat( character.FirstName, ",", character.LastName );
				Log.Warn( $"{nameStr} breached gateLock[{id}]" );
				GateStates[id] = true;
				source.TriggerEvent( "GateManager.ToggleBreach", id );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		private static void InitializeGateLocks( [FromSource] Player source ) {
			try {
				if( GateStates.Any() ) {
					source.TriggerEvent( "GateManager.InitializeGateLocks", JsonConvert.SerializeObject( GateStates ) );
				}
			}
			catch( Exception ex ) {
				Log.Error(ex);
			}
		}
	}
}
