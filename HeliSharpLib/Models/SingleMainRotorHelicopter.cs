using System;
using MathNet.Numerics.LinearAlgebra;
using System.Web.Script.Serialization;
using Newtonsoft.Json;

namespace HeliSharp
{

	/// Force system model of a generic single main rotor helicopter.
	///	Essentially a ForceAssembly extended with some methods for superimposing main rotor
	///	downwash on local velocities of submodels, trimming interface, utility functions etc.

	[Serializable]
	public class SingleMainRotorHelicopter : ForceAssembly
	{
		// Control inputs
		[JsonIgnore]
		public double Collective { // positive up
			get { return FCS.CollectiveCommand; }
			set { FCS.CollectiveCommand = value; }
		}
		[JsonIgnore]
		public double LongCyclic { // positive forward
			get { return FCS.LongCommand; }
			set { FCS.LongCommand = value; }
		}
		[JsonIgnore]
		public double LatCyclic { // positive right
			get { return FCS.LatCommand; }
			set { FCS.LatCommand = value; }
		}
		[JsonIgnore]
		public double Pedal { // positive right
			get { return FCS.PedalCommand; }
			set { FCS.PedalCommand = value; }
		}

		public override Matrix<double> Rotation {
			get { return base.Rotation; }
			set {
				base.Rotation = value;
				Attitude = value.EulerAngles();
			}
		}

		// Attitude needs to be set from rigid body simulation
		// phi (roll +right), theta (pitch +down), psi (heading +right)

		private Vector<double> attitude = Vector<double>.Build.Zero3();
		[JsonIgnore]
		public Vector<double> Attitude {
			get {
				return attitude;
			}
			set { 
				attitude = value;
				if (attitude[0] > Math.PI) attitude[0] -= 2 * Math.PI;
				if (attitude[0] < -Math.PI) attitude[0] += 2 * Math.PI;
				if (attitude[1] > Math.PI) attitude[1] -= 2 * Math.PI;
				if (attitude[1] < -Math.PI) attitude[1] += 2 * Math.PI;
				base.Rotation = Matrix<double>.Build.EulerRotation(attitude);
				Gravity.Rotation = Matrix<double>.Build.EulerRotation(-attitude[0], -attitude[1], -attitude[2]);
			}
		}
		[JsonIgnore]
		public double RollAngle { // positive right
			get { return attitude[0]; } 
			set { attitude[0] = value; }
		}
		[JsonIgnore]
		public double PitchAngle { // positive up
			get { return attitude[1]; } 
			set { attitude[1] = value; }
		}
		[JsonIgnore]
		public double Heading { // positive right
			get { return attitude[2]; }
			set { attitude[2] = value; }
		}

		[JsonIgnore]
		public Vector<double> Wind { get; set; }

		public Vector<double> GroundVelocity {
			set {
				Matrix<double> R = Matrix<double>.Build.EulerRotation(attitude);
				Velocity = value - R * Wind;
			}
		}
		public Vector<double> AbsoluteVelocity {
			set {
				GroundVelocity = Gravity.Rotation * value;
			}
		}

		[JsonIgnore]
		public double Height { 
			get { return MainRotor.HeightAboveGround + MainRotor.Translation[2]; }
			set { MainRotor.HeightAboveGround = value - MainRotor.Translation[2]; } 
		}

		private double mass;
		public double Mass { get { return mass; } set { mass = value; Gravity.Force[2] = mass*9.81; } }

		[JsonIgnore]
		public Matrix<double> Inertia { get; set; }

		// For serialization
		[JsonProperty("inertia")]
		public double[] InertiaD {
			get { return Inertia.ToColumnWiseArray(); }
			set { Inertia = Matrix<double>.Build.DenseOfColumnMajor(3, 3, value); }
		}



		public FlightControlSystem FCS { get; set; }

		public bool UseEngineModel { get; set; }
		public GearBox GearBox { get; set; }
		public Engine Engine { get; set; }

		// Sub-models

		private Rotor mainRotor;
		public Rotor MainRotor {
			get { return mainRotor; }
			set { mainRotor = value; SetModel("MainRotor", value); } 
		}
		private Rotor tailRotor;
		public Rotor TailRotor {
			get { return tailRotor; }
			set { tailRotor = value; SetModel("TailRotor", value); } 
		}
		private Stabilizer horizontalStabilizer;
		public Stabilizer HorizontalStabilizer {
			get { return horizontalStabilizer; }
			set { horizontalStabilizer = value; SetModel("HorizontalStabilizer", value); }
		}
		private Stabilizer verticalStabilizer;
		public Stabilizer VerticalStabilizer {
			get { return verticalStabilizer; }
			set { verticalStabilizer = value; SetModel("VerticalStabilizer", value); }
		}
		private Fuselage fuselage;
		public Fuselage Fuselage {
			get { return fuselage; }
			set { fuselage = value; SetModel("Fuselage", value); }
		}
		[JsonIgnore]
		public StaticForce Gravity { get; private set; }
		[JsonIgnore]
		public Atmosphere Atmosphere { get; private set; }

		[JsonIgnore]
		public double PowerRequired { get; private set; }


		public SingleMainRotorHelicopter ()
		{
			Wind = Vector<double>.Build.Zero3();
			MainRotor = new Rotor();
			TailRotor = new Rotor();
			Gravity = new StaticForce(Vector<double>.Build.Dense3(0, 0, Mass * 9.81));
			SetModel ("Gravity", Gravity);
			Atmosphere = new Atmosphere();
		}

		public SingleMainRotorHelicopter LoadDefault()
		{
			Mass = 2450;
			Inertia = Matrix<double>.Build.DenseOfArray (new double[,] {
				{ 1762, 0, 1085 },
				{ 0, 9167, 0 },
				{ 1085, 0, 8687 }
			});
			MainRotor = new Rotor().LoadDefault();
			MainRotor.Translation = Vector<double>.Build.DenseOfArray(new double[] { 0.0071, 0, -1.5164 });
			MainRotor.Rotation = Matrix<double>.Build.RotationY(-6.3 * Math.PI / 180.0);
			TailRotor = new Rotor().LoadDefaultTailRotor();
			TailRotor.Translation = Vector<double>.Build.DenseOfArray (new double[] { -7.5, 0, -0.8001 });
			TailRotor.Rotation =  Matrix<double>.Build.DenseOfArray(new double[,] {
				{ 1, 0, 0 },
				{ 0, 0, -1 },
				{ 0, 1, 0 }
			});
			HorizontalStabilizer = new Stabilizer().LoadDefaultHorizontal();
			HorizontalStabilizer.Translation = Vector<double>.Build.DenseOfArray(new double[] { -5.8, 0, -0.5 });
			HorizontalStabilizer.Rotation = Matrix<double>.Build.RotationY(5 * Math.PI / 180.0);
			VerticalStabilizer = new Stabilizer().LoadDefaultVertical();
			VerticalStabilizer.Translation = Vector<double>.Build.DenseOfArray(new double[] { -7.3, 0, -1.5 });
			VerticalStabilizer.Rotation = Matrix<double>.Build.RotationX(90 * Math.PI / 180.0) * Matrix<double>.Build.RotationY(5 * Math.PI / 180.0);

			Fuselage = new Fuselage().LoadDefault();
			Fuselage.Translation = Vector<double>.Build.DenseOfArray(new double[] { 0.0178, 0, 0.0127 });
			Fuselage.mrdistance = MainRotor.Translation - Fuselage.Translation;

			FCS = new FlightControlSystem().LoadDefault();
			Engine = new Engine().LoadDefault();
			GearBox = new GearBox().LoadDefault();

			return this;
		}

		public void InitEngine(bool running) {
			UseEngineModel = true;
			GearBox.MainRotorRatio = Engine.Omega0 / MainRotor.designOmega;
			GearBox.TailRotorRatio = Engine.Omega0 / TailRotor.designOmega;
			if (running) {
				MainRotor.RotSpeed = MainRotor.designOmega;
				TailRotor.RotSpeed = TailRotor.designOmega;
				Engine.Init(600);
			} else
				Engine.InitStopped();
		}

		public override void Update(double dt)
		{
			// Update atmospheric properties
			Atmosphere.Position = Translation;
			Atmosphere.Update(dt);
			mainRotor.Density = Atmosphere.Density;
			tailRotor.Density = Atmosphere.Density;
			if (fuselage != null) fuselage.Density = Atmosphere.Density;
			if (horizontalStabilizer != null) horizontalStabilizer.Density = Atmosphere.Density;
			if (verticalStabilizer != null) verticalStabilizer.Density = Atmosphere.Density;

			// Update FCS
			FCS.Velocity = Velocity;
			FCS.AngularVelocity = AngularVelocity;
			FCS.Attitude = Attitude;
			FCS.Update(dt);
			mainRotor.Collective = FCS.Collective;
			mainRotor.LongCyclic = FCS.LongCyclic;
			mainRotor.LatCyclic = FCS.LatCyclic;
			tailRotor.Collective = -FCS.Pedal; // TODO make pedal-collective inversion a parameter

			// Update engine and drivetrain
			if (UseEngineModel) {
                if (Engine.phase == Engine.Phase.START && GearBox.autoBrakeOmega > 1e-5) GearBox.BrakeEnabled = false;
				GearBox.MainRotorLoad = MainRotor.ShaftTorque;
				GearBox.TailRotorLoad = TailRotor.ShaftTorque;
				GearBox.MainRotorInertia = MainRotor.Inertia;
				GearBox.TailRotorInertia = TailRotor.Inertia;
				GearBox.Update(dt);
				Engine.load = GearBox.Load;
				Engine.inertia = GearBox.Inertia;
				Engine.Update(dt);
				GearBox.RotspeedDrive = Engine.rotspeed;
				MainRotor.RotSpeed = GearBox.MainRotorSpeed;
				TailRotor.RotSpeed = GearBox.TailRotorSpeed;
			}

			base.Update (dt);

			// Calculate power required
			if (UseEngineModel)
				PowerRequired = GearBox.Load * GearBox.RotspeedDrive;
			else
				PowerRequired = mainRotor.ShaftTorque * mainRotor.RotSpeed + tailRotor.ShaftTorque * tailRotor.RotSpeed
					+ 0.1 * 628 * 628; // + gearbox.getFriction()*engine.getDesignRotationSpeed()*engine.getDesignRotationSpeed();

			// Add tail rotor torque and remove torque when clutch is disengaged
			if (UseEngineModel && !GearBox.engaged)
				Torque[2] -= MainRotor.Torque[2];
			else if (TailRotor.RotSpeed > 0.1)
				Torque[2] += TailRotor.Torque[2] * MainRotor.RotSpeed / TailRotor.RotSpeed;
		}


		public void TrimInit()
		{
			MainRotor.beta_0 = 3.0 * Math.PI / 180.0;
			MainRotor.beta_cos = 0.0;
			MainRotor.beta_sin = 0.0;
			MainRotor.RotSpeed = MainRotor.designOmega;
			double CTguess = Mass * 9.81 / MainRotor.RhoAOR2;
			MainRotor.CT = TailRotor.CT = CTguess;
			TailRotor.beta_0 = 3.0 * Math.PI / 180.0;
			TailRotor.beta_cos = 0.0;
			TailRotor.beta_sin = 0.0;
			TailRotor.RotSpeed = TailRotor.designOmega;

			Collective = 0.5;
			LongCyclic = 0.0;
			LatCyclic = 0.0;
			Pedal = -0.5;
		}

		public void Trim()
		{
			bool fcs = FCS.enabled;
			FCS.enabled = false;
			bool mainDynamicInflow = MainRotor.useDynamicInflow;
			MainRotor.useDynamicInflow = false;
			bool tailDynamicInflow = TailRotor.useDynamicInflow;
			TailRotor.useDynamicInflow = false;
			bool engineModel = UseEngineModel;
			UseEngineModel = false;
			bool gravity = Gravity.Enabled;
			Gravity.Enabled = true;

			Vector<double> initialGuess = Vector<double>.Build.DenseOfArray(new double[] {
				Collective, LatCyclic, LongCyclic, Pedal, Attitude[0], Attitude[1]
			});

			new JacobianTrimmer().Trim(
				initialGuess,
				Vector<double>.Build.DenseOfArray (new double[] { 1e-3, 1e-3, 1e-3, 1e-3, 1e-3, 1e-3 }),
				delegate (Vector<double> x) {
					// Set input variables from input vector, x=(t0 ts tc tp phi theta)
					Collective = x[0];
					LongCyclic = x[1];
					LatCyclic = x[2];
					Pedal = x[3];
					Attitude = Vector<double>.Build.DenseOfArray(new double[] { x[4], x[5], Attitude[2] });
					// Update
					for (int i = 0; i < 10; i++) {
						Update(0.01);
					}
					// Set output vector
					return Vector<double>.Build.DenseOfArray (new double[] {
						Force[0], Force[1], Force[2],
						Torque[0], Torque[1], Torque[2]
					});
				}
			);

			FCS.TrimCollective = Collective;
			FCS.TrimLongCyclic = LongCyclic;
			FCS.TrimLatCyclic = LatCyclic;
			FCS.TrimPedal = Pedal;
			FCS.TrimAttitude = Attitude.Clone();
			if (FCS.trimControl) {
				FCS.CollectiveCommand = 0;
				FCS.LongCommand = 0;
				FCS.LatCommand = 0;
				FCS.PedalCommand = 0;
			}
			FCS.enabled = fcs;
			MainRotor.useDynamicInflow = mainDynamicInflow;
			TailRotor.useDynamicInflow = tailDynamicInflow;
			UseEngineModel = engineModel;
			Gravity.Enabled = gravity;
		}

		override protected void updateLocalVelocity (ForceModel model)
		{
			if (model == mainRotor) {
				base.updateLocalVelocity(model);
				return;
			}
			double dMR = (model.Translation - mainRotor.Translation).Norm(2);
			Vector<double> washvel = mainRotor.Rotation * mainRotor.GetDownwashVelocity(dMR);
			if (washvel.Norm(2) <= 0.01) {
				base.updateLocalVelocity(model);
				return;
			}
			// Ramp downwash velocity to zero from distance to center 0.9 to 1.1, to avoid too abrupt changes
			double dWashCenter = washvel.Cross(mainRotor.Translation - model.Translation).Norm(2) / washvel.Norm(2);
			double Rdw = mainRotor.GetDownwashRadius(dMR);
			double ndwc = dWashCenter / Rdw;
			if (ndwc > 1.1)
				washvel = Vector<double>.Build.Zero3();
			else if (ndwc > 0.9)
				washvel *= 1.0-(ndwc-0.9)/(1.1-0.9);

			// Compensate for wake acceleration (see Dreier figure 12.7) - this is really improvised
			// i.e. tilt wash vector downward further from main rotor
			double w_w0 = washvel.Norm(2)/mainRotor.GetDownwashVelocity(0).Norm(2)-1.0;
			double skew = Math.Atan2(washvel.x(),-washvel.z());
			Matrix<double> R = Matrix<double>.Build.RotationY(skew * w_w0);
			washvel = R*washvel;

			model.AngularVelocity = model.InvRotation * AngularVelocity;
			model.Velocity = model.InvRotation * (Velocity + washvel - model.Translation.Cross (AngularVelocity));
		}

		public void GetControlAngles(out double theta_0, out double theta_sin, out double theta_cos, out double theta_p)
		{
			MainRotor.GetControlAngles(out theta_0, out theta_sin, out theta_cos);
			double dummy;
			TailRotor.GetControlAngles(out theta_p, out dummy, out dummy);
		}

		public void SetControlAngles(double theta_0, double theta_sin, double theta_cos, double theta_p)
		{
			double ntheta_0,ntheta_sin,ntheta_cos,ntheta_p,dummy;
			MainRotor.GetNormalizedControlAngles(theta_0, theta_sin, theta_cos, out ntheta_0, out ntheta_sin, out ntheta_cos);
			TailRotor.GetNormalizedControlAngles(theta_p, 0, 0, out ntheta_p, out dummy, out dummy);
			Collective = ntheta_0;
			LongCyclic = ntheta_sin;
			LatCyclic = ntheta_cos;
			Pedal = -ntheta_p; // TODO make pedal-collective inversion a parameter
		}
	}
}
