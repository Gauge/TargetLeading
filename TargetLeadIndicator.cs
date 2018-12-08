using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TargetLeading
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {
        private List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        private Dictionary<long, IMyGps> _gpsPoints = new Dictionary<long, IMyGps>();
        private bool _wasInTurretLastFrame = false;
        private LeadingData CurrentData = null;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd += AddGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveGrid;
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd -= AddGrid;
            MyAPIGateway.Entities.OnEntityRemove -= RemoveGrid;
        }

        private void AddGrid(IMyEntity ent)
        {
            IMyCubeGrid grid = ent as IMyCubeGrid;
            if (grid != null)
            {
                _grids.Add(grid);
            }
        }

        private void RemoveGrid(IMyEntity ent)
        {
            IMyCubeGrid grid = ent as IMyCubeGrid;
            if (grid != null && _grids.Contains(grid))
            {
                _grids.Remove(grid);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            CurrentData = LeadingData.GetLeadingData(MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity, CurrentData);

            if (CurrentData == null)
            {
                ClearGPS();
                return;
            }

            _wasInTurretLastFrame = true;

            if (CurrentData.Ammo == null)
            {
                ClearGPS();
                return;
            }

            float projectileSpeed = CurrentData.Ammo.DesiredSpeed;
            float projectileRange = CurrentData.Ammo.MaxTrajectory + 100;
            float projectileRangeSquared = projectileRange * projectileRange;

            foreach (IMyCubeGrid grid in _grids)
            {
                if (grid.Physics == null)
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D gridLoc = grid.WorldAABB.Center;

                if (grid.EntityId == CurrentData.EntityId
                    || Vector3D.DistanceSquared(gridLoc, CurrentData.Position) > projectileRangeSquared
                    || !GridHasHostileOwners(grid))
                {
                    RemoveGPS(grid.EntityId);
                    continue;
                }

                Vector3D interceptPoint = CalculateProjectileInterceptPosition(
                    projectileSpeed,
                    CurrentData.Velocity,
                    CurrentData.Position,
                    grid.Physics.LinearVelocity,
                    gridLoc);

                AddGPS(grid.EntityId, interceptPoint);
            }
        }

        public static bool GridHasHostileOwners(IMyCubeGrid grid)
        {
            var gridOwners = grid.BigOwners;
            foreach (var pid in gridOwners)
            {
                MyRelationsBetweenPlayerAndBlock relation = MyAPIGateway.Session.Player.GetRelationTo(pid);
                if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    return true;
                }
            }
            return false;
        }

        // Whip's CalculateProjectileInterceptPosition Method
        // Uses vector math as opposed to the quadratic equation
        private static Vector3D CalculateProjectileInterceptPosition(
            double projectileSpeed,
            Vector3D shooterVelocity,
            Vector3D shooterPosition,
            Vector3D targetVelocity,
            Vector3D targetPos,
            double interceptPointMultiplier = 1)
        {
            var directHeading = targetPos - shooterPosition;
            var directHeadingNorm = Vector3D.Normalize(directHeading);

            var relativeVelocity = targetVelocity - shooterVelocity;

            var parallelVelocity = relativeVelocity.Dot(directHeadingNorm) * directHeadingNorm;
            var normalVelocity = relativeVelocity - parallelVelocity;

            var diff = projectileSpeed * projectileSpeed - normalVelocity.LengthSquared();
            if (diff < 0)
                return normalVelocity;

            var projectileForwardVelocity = Math.Sqrt(diff) * directHeadingNorm;

            var timeToIntercept = interceptPointMultiplier * Math.Abs(Vector3D.Dot(directHeading, directHeadingNorm)) / Vector3D.Dot(projectileForwardVelocity, directHeadingNorm);

            return shooterPosition + timeToIntercept * (projectileForwardVelocity + normalVelocity);
        }

        private void AddGPS(long gridId, Vector3D target)
        {
            if (!_gpsPoints.ContainsKey(gridId))
            {
                _gpsPoints.Add(gridId, MyAPIGateway.Session.GPS.Create(gridId.ToString(), "", target, true));
                MyAPIGateway.Session.GPS.AddLocalGps(_gpsPoints[gridId]);
                MyVisualScriptLogicProvider.SetGPSColor(gridId.ToString(), Color.Orange);
                _gpsPoints[gridId].Name = "";
            }

            _gpsPoints[gridId].Coords = target;
        }

        private void ClearGPS()
        {
            if (!_wasInTurretLastFrame)
                return;

            foreach (IMyGps gps in _gpsPoints.Values)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
            }
            _gpsPoints.Clear();

            _wasInTurretLastFrame = false;
        }

        private void RemoveGPS(long id)
        {
            if (_gpsPoints.ContainsKey(id))
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(_gpsPoints[id]);
                _gpsPoints.Remove(id);
            }
        }
    }

    public class LeadingData
    {
        public long EntityId;
        public MyAmmoDefinition Ammo = null;
        public Vector3D Position = Vector3D.Zero;
        public Vector3D Velocity = Vector3D.Zero;

        internal Vector3D offset = Vector3D.Zero;
        internal int toolbarIndex = -1;

        public static LeadingData GetLeadingData(IMyEntity block, LeadingData current)
        {
            try
            {
                if (block is IMyLargeTurretBase)
                {
                    IMyLargeTurretBase turret = block as IMyLargeTurretBase;

                    return new LeadingData()
                    {
                        EntityId = turret.EntityId,
                        Position = turret.GetPosition(),
                        Velocity = turret.CubeGrid.Physics.LinearVelocity,
                        Ammo = (turret as IMyGunObject<MyGunBase>).GunBase.CurrentAmmoDefinition
                    };
                }
                else if (block is IMyShipController)
                {
                    IMyShipController cockpit = block as IMyShipController;

                    LeadingData data = new LeadingData
                    {
                        EntityId = block.EntityId,
                        Velocity = cockpit.CubeGrid.Physics.LinearVelocity,
                        Position = cockpit.CubeGrid.WorldAABB.Center
                    };

                    if (current != null)
                    {
                        SerializableDefinitionId definition = GetWeaponDef(cockpit, ref data.toolbarIndex);
                        if (current.toolbarIndex != data.toolbarIndex)
                        {
                            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                            cockpit.CubeGrid.GetBlocks(blocks, (b) =>
                            {
                                return b.BlockDefinition.Id == definition &&
                                b.FatBlock != null &&
                                b.FatBlock.Orientation.Forward == cockpit.Orientation.Forward;
                            });

                            foreach (IMySlimBlock b in blocks)
                            {
                                data.offset += data.Position - b.FatBlock.PositionComp.WorldAABB.Center;

                                if (data.Ammo == null)
                                {
                                    data.Ammo = (b.FatBlock as IMyGunObject<MyGunBase>).GunBase.CurrentAmmoDefinition;
                                }
                            }

                            data.offset = data.offset / blocks.Count;
                        }
                        else
                        {
                            data.offset = current.offset;
                            data.Ammo = current.Ammo;
                        }
                    }

                    data.Position = data.Position - data.offset;
                    return data;
                }
            }
            catch (Exception e)
            {
                // bad fix i know
                //MyLog.Default.Info(e.ToString());
            }

            return null;
        }

        public static SerializableDefinitionId GetWeaponDef(IMyShipController cockpit, ref int index)
        {
            MyObjectBuilder_ShipController builder = cockpit.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_ShipController;

            if (builder == null || builder.Toolbar == null || !builder.Toolbar.SelectedSlot.HasValue)
            {
                return default(SerializableDefinitionId);
            }

            MyObjectBuilder_Toolbar toolbar = builder.Toolbar;
            var item = toolbar.Slots[toolbar.SelectedSlot.Value];
            if (!(item.Data is MyObjectBuilder_ToolbarItemWeapon))
            {
                return default(SerializableDefinitionId);
            }

            index = toolbar.SelectedSlot.Value;
            return (item.Data as MyObjectBuilder_ToolbarItemWeapon).defId;
        }
    }
}
