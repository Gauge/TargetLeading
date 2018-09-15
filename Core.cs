using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace TargetLeading
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            IMyLargeTurretBase turret = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity as IMyLargeTurretBase;
            if (turret == null) return;

            IMyGunObject<MyGunBase> gunBase = turret as IMyGunObject<MyGunBase>;

            Vector3D turretLoc = turret.GetPosition();
            float speed = gunBase.GunBase.CurrentAmmoDefinition.SpeedVar;
            float range = gunBase.GunBase.CurrentAmmoDefinition.MaxTrajectory;
            BoundingSphereD sphere = new BoundingSphereD(turretLoc, range);

            HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, (e) =>
            {
                return (e is IMyCubeGrid) 
                && e.EntityId != turret.CubeGrid.EntityId
                && e.GetIntersectionWithSphere(ref sphere)
                //&& MyAPIGateway.Players.GetPlayerControllingEntity(e) != null
                ;
            });

            foreach (IMyEntity entity in grids)
            {
                IMyCubeGrid grid = entity as IMyCubeGrid;

                if (grid.Physics == null) continue;

                //DrawDot()
            }
        }

        private void DrawDot(Vector3D target)
        {

        }
    }
}
