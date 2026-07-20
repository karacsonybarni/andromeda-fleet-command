using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Missions;

public static class MissionCatalog
{
    public static MissionDefinition FleetDuel { get; } = new(
        MissionId.FleetDuel,
        "Fleet Duel",
        "Balanced multiplayer engagement",
        "Two command crews enter a mirrored fleet engagement. Break the opposing flagship while preserving your own.",
        "All ships, attack the enemy flagship",
        0xAFC4004,
        [
            Player("player-flagship", "Andromeda Flagship", ShipClass.Flagship, 250, 450),
            Player("player-carrier", "Andromeda Carrier", ShipClass.Carrier, 220, 250),
            Player("player-frigate", "Andromeda Frigate", ShipClass.Frigate, 340, 335),
            Player("player-destroyer", "Andromeda Destroyer", ShipClass.Destroyer, 280, 640),
            Enemy("enemy-flagship", "Ketzal Flagship", ShipClass.Flagship, 1350, 450),
            Enemy("enemy-carrier", "Ketzal Carrier", ShipClass.Carrier, 1380, 250),
            Enemy("enemy-frigate", "Ketzal Frigate", ShipClass.Frigate, 1260, 335),
            Enemy("enemy-destroyer", "Ketzal Destroyer", ShipClass.Destroyer, 1320, 640)
        ],
        [
            new("player-carrier", OrderType.Defend, "player-flagship"),
            new("player-frigate", OrderType.Intercept, "enemy-frigate"),
            new("player-destroyer", OrderType.Attack, "enemy-flagship"),
            new("enemy-carrier", OrderType.Defend, "enemy-flagship"),
            new("enemy-frigate", OrderType.Intercept, "player-frigate"),
            new("enemy-destroyer", OrderType.Attack, "player-flagship")
        ],
        new(MissionObjectiveKind.DestroyTarget,
            "Destroy the opposing flagship",
            "Break the opposing command ship while preserving your flagship.",
            TargetId: "enemy-flagship",
            ProtectedShipId: "player-flagship"),
        new(
            "MULTIPLAYER  •  FLEET DUEL",
            "HOST-AUTHORITATIVE ENGAGEMENT",
            [
                "Two player-led fleets have entered weapons range.",
                "Coordinate your assigned hulls and destroy the opposing flagship."
            ],
            [
                "The opposing flagship is disabled and its fleet is withdrawing.",
                "Your command crew controls the battlespace."
            ],
            [
                "Your flagship is disabled and command authority is lost.",
                "The opposing fleet controls the battlespace."
            ]),
        new(3, "PLAYER VERSUS PLAYER", 3,
            "Coordinate a balanced four-ship fleet against another command crew."),
        0);

    public static IReadOnlyList<MissionDefinition> All { get; } =
    [
        new(
            MissionId.FirstCommand,
            "First Command",
            "Training patrol, Nysa Reach",
            "Raiders have trapped a civilian convoy. Take direct control, switch ships, then order the frigate to intercept their bomber before the raider leader escapes.",
            "Frigate Two, intercept the bomber",
            0xAFC1001,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 250, 460),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 300, 330),
                Enemy("enemy-raider-leader", "Raider Leader", ShipClass.Destroyer, 1240, 460),
                Enemy("enemy-bomber-1", "Raider Bomber", ShipClass.Bomber, 1090, 290),
                Enemy("enemy-escort-1", "Raider Escort", ShipClass.Escort, 1150, 610)
            ],
            [
                new("player-frigate", OrderType.Defend, "player-flagship"),
                new("enemy-raider-leader", OrderType.Attack, "player-flagship"),
                new("enemy-bomber-1", OrderType.Attack, "player-flagship"),
                new("enemy-escort-1", OrderType.Attack, "player-frigate")
            ],
            new(MissionObjectiveKind.DestroyTarget,
                "Disable the raider leader",
                "Destroy the raider command ship while keeping your flagship alive.",
                TargetId: "enemy-raider-leader",
                ProtectedShipId: "player-flagship"),
            new(
                "ACT I  •  THE BLACK SUN INCIDENT",
                "ADMIRAL SERA VEY  //  ANDROMEDA FLEET COMMAND",
                [
                    "Nysa Seven found an impossible star map inside a raider beacon.",
                    "Save the convoy and seize their command core before every witness is erased."
                ],
                [
                    "The core carries a Ketzal military cipher and one phrase: BLACK SUN.",
                    "Its next coordinate is Pelagos Corridor. Carrier One is already there."
                ],
                [
                    "The convoy and its beacon are gone. The raiders are erasing the trail.",
                    "Regroup, Captain. We need that command core."
                ]),
            new(1, "ORIENTATION", 1,
                "Learn direct control, switch hulls, issue one order, and trigger an ability."),
            15),
        new(
            MissionId.BrokenShield,
            "Broken Shield",
            "Convoy defence, Pelagos Corridor",
            "A Ketzal bomber wing is diving on Carrier One. Split the fleet: intercept the bombers, defend the carrier, and use each ship's tactical role.",
            "Frigate Two, intercept the bomber wing",
            0xAFC2002,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 245, 450),
                Player("player-carrier", "Carrier One", ShipClass.Carrier, 205, 270),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 345),
                Player("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 270, 625),
                Enemy("enemy-bomber-1", "Bomber One", ShipClass.Bomber, 1180, 190),
                Enemy("enemy-bomber-2", "Bomber Two", ShipClass.Bomber, 1240, 270),
                Enemy("enemy-bomber-3", "Bomber Three", ShipClass.Bomber, 1200, 690),
                Enemy("enemy-bomber-4", "Bomber Four", ShipClass.Bomber, 1280, 610),
                Enemy("enemy-escort-1", "Escort One", ShipClass.Escort, 1320, 390),
                Enemy("enemy-escort-2", "Escort Two", ShipClass.Escort, 1350, 520)
            ],
            [
                new("player-carrier", OrderType.Defend, "player-flagship"),
                new("player-frigate", OrderType.Intercept, "enemy-bomber-1"),
                new("player-destroyer", OrderType.Attack, "enemy-bomber-3"),
                new("enemy-bomber-1", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-2", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-3", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-4", OrderType.Attack, "player-carrier"),
                new("enemy-escort-1", OrderType.Attack, "player-flagship"),
                new("enemy-escort-2", OrderType.Attack, "player-destroyer")
            ],
            new(MissionObjectiveKind.EliminateClass,
                "Break the bomber wing",
                "Destroy every bomber before Carrier One is lost.",
                TargetClass: ShipClass.Bomber,
                ProtectedShipId: "player-carrier"),
            new(
                "ACT I  •  THE BLACK SUN INCIDENT",
                "ADMIRAL SERA VEY  //  PRIORITY TRANSMISSION",
                [
                    "Pelagos is not a raid. It is a door, and the Ketzal are forcing it open.",
                    "Keep Carrier One alive long enough to finish decoding the stolen cipher."
                ],
                [
                    "Carrier One has the answer: Black Sun is masking an invasion corridor.",
                    "The Ketzal command fleet is crossing now. We have one jump to intercept."
                ],
                [
                    "Carrier One is lost, and Black Sun has vanished behind its own signal.",
                    "Return to Pelagos before the invasion corridor stabilizes."
                ]),
            new(2, "SPLIT DEFENCE", 2,
                "Protect a mobile carrier while multiple wings attack from separate axes."),
            18),
        new(
            MissionId.BlackSun,
            "Black Sun",
            "Fleet engagement, Andromeda frontier",
            "The Ketzal flagship is exposed, but its carrier and escorts form a layered defence. Command the entire fleet, protect your flagship, and end the engagement.",
            "All ships, attack the enemy flagship",
            0xAFC3003,
            [
                Player("player-flagship", "Flagship", ShipClass.Flagship, 260, 450),
                Player("player-carrier", "Carrier One", ShipClass.Carrier, 205, 265),
                Player("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 335),
                Player("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 260, 620),
                Enemy("enemy-flagship", "Enemy Flagship", ShipClass.Flagship, 1340, 450),
                Enemy("enemy-carrier", "Enemy Carrier", ShipClass.Carrier, 1390, 235),
                Enemy("enemy-destroyer-1", "Enemy Destroyer One", ShipClass.Destroyer, 1250, 300),
                Enemy("enemy-destroyer-2", "Enemy Destroyer Two", ShipClass.Destroyer, 1270, 650),
                Enemy("enemy-bomber-1", "Bomber One", ShipClass.Bomber, 1170, 220),
                Enemy("enemy-bomber-2", "Bomber Two", ShipClass.Bomber, 1210, 185),
                Enemy("enemy-bomber-3", "Bomber Three", ShipClass.Bomber, 1205, 730),
                Enemy("enemy-escort-1", "Enemy Escort One", ShipClass.Escort, 1300, 540),
                Enemy("enemy-escort-2", "Enemy Escort Two", ShipClass.Escort, 1320, 580)
            ],
            [
                new("player-carrier", OrderType.Defend, "player-flagship"),
                new("player-frigate", OrderType.Intercept, "enemy-bomber-1"),
                new("player-destroyer", OrderType.Attack, "enemy-flagship"),
                new("enemy-flagship", OrderType.Hold),
                new("enemy-carrier", OrderType.Defend, "enemy-flagship"),
                new("enemy-destroyer-1", OrderType.Defend, "enemy-flagship"),
                new("enemy-destroyer-2", OrderType.Attack, "player-flagship"),
                new("enemy-bomber-1", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-2", OrderType.Attack, "player-carrier"),
                new("enemy-bomber-3", OrderType.Attack, "player-carrier"),
                new("enemy-escort-1", OrderType.Attack, "player-frigate"),
                new("enemy-escort-2", OrderType.Attack, "player-destroyer")
            ],
            new(MissionObjectiveKind.DestroyTarget,
                "Destroy the enemy flagship",
                "Break the Ketzal command ship while your flagship survives.",
                TargetId: "enemy-flagship",
                ProtectedShipId: "player-flagship"),
            new(
                "ACT I  •  THE BLACK SUN INCIDENT",
                "ADMIRAL SERA VEY  //  FRONTIER EMERGENCY",
                [
                    "Black Sun is holding the invasion corridor open behind a layered fleet.",
                    "Break its defenders, preserve our flagship, and sever the Ketzal route."
                ],
                [
                    "Black Sun is dark. The invasion corridor is collapsing behind its fleet.",
                    "Its final burst said: ‘Crown Fleet, awaken.’ We won the corridor—not the war."
                ],
                [
                    "Our flagship is down and Black Sun still broadcasts into the frontier.",
                    "The invasion clock is moving. Return before the corridor locks open."
                ]),
            new(3, "FLEET COORDINATION", 3,
                "Manage four roles, layered defenders, protected assets, and a timed ability salvo."),
            20),
        ..ExtendedSpecs.Select(CreateExtendedMission)
    ];

    // Expression-bodied so the campaign initializer can safely build these specs
    // before later static members would otherwise receive their backing values.
    private static IReadOnlyList<ExtendedMissionSpec> ExtendedSpecs =>
    [
        new(MissionId.Afterglow, "Afterglow", "Rescue line, ruined Pelagos",
            "Crown Fleet's wake has crippled the Pelagos habitats. Hold the rescue lane while Carrier One recovers survivors and the bomber screen closes in.",
            "Frigate Two, intercept the nearest bomber", 0xAFC4004, Encounter.BomberDefence, 2,
            "ACT II  •  CROWN WAKES", "DISASTER RESPONSE",
            "Black Sun collapsed, but something older answered from beneath Pelagos.",
            "Keep the rescue carrier alive. Every civilian we recover is a witness.",
            "The last shuttle carries a recording older than either human or Ketzal settlement.",
            "It names the black-hole seeds as anchors—and warns that Crown Fleet has begun removing them.",
            "The rescue lane is broken and Pelagos is falling into the afterglow.",
            "Re-form the screen before Crown Fleet erases the surviving record.", 16,
            "Defend rescue operations while prioritizing bombers across two approach lanes."),
        new(MissionId.SilentRelay, "Silent Relay", "Covert strike, Orison darkspace",
            "A dormant relay is transmitting seed locations to Crown Fleet. Break its command ship before it finishes the atlas.",
            "All ships, attack the enemy flagship", 0xAFC5005, Encounter.CommandStrike, 2,
            "ACT II  •  CROWN WAKES", "SIGNALS INTELLIGENCE",
            "The Pelagos recording points to a relay that has been silent for three centuries.",
            "It woke with Crown Fleet. Cut the transmission before the seed atlas is complete.",
            "The relay is dark, and its partial atlas points toward a Ketzal vault at Crownfall.",
            "A second signal is hidden inside it: Admiral Vey, do not trust Human Command.",
            "The relay completed another section of Crown Fleet's atlas.",
            "We must strike again before it maps the next inhabited system.", 16,
            "Execute a fast command-ship strike before its escorts can consolidate."),
        new(MissionId.Crownfall, "Crownfall Ambush", "Ketzal vault approach, Crownfall",
            "The Ketzal vault fleet has mistaken us for Crown Fleet's vanguard. Survive the ambush and disable its flagship long enough to open communications.",
            "All ships, attack the enemy flagship", 0xAFC6006, Encounter.CommandStrike, 3,
            "ACT II  •  CROWN WAKES", "FIRST CONTACT UNDER FIRE",
            "The Crownfall vault holds the only intact history of the seed network.",
            "Their commander, Kael Orun, will not answer while our weapons remain untested.",
            "Orun yields the channel. Crown Fleet is not Ketzal—it is the network's ancient custodian.",
            "It has judged both civilizations dangerous and is reclaiming every anchor.",
            "Orun closes the vault and marks us as another raiding fleet.",
            "Break the ambush without destroying the archive we came to save.", 18,
            "Fight through a disciplined screen while preserving the possibility of an alliance."),

        new(MissionId.HollowMoon, "The Hollow Moon", "Evacuation, Crownfall archive",
            "Crown Fleet has cracked the moon around Orun's archive. Protect Carrier One while the joint evacuation extracts its memory core.",
            "Frigate Two, intercept the nearest bomber", 0xAFC7007, Encounter.BomberDefence, 3,
            "ACT III  •  THE SEED WAR", "JOINT EVACUATION",
            "Crownfall's moon is a shell around an anchor-control archive.",
            "Human and Ketzal crews are boarding the same carrier. Keep it alive.",
            "The archive confirms the seeds stabilize nearby stars and habitable worlds.",
            "Mining them for power has been shortening the lives of both civilizations.",
            "The archive core is lost beneath the collapsing moon.",
            "Without it, Crown Fleet remains the only force that understands the network.", 17,
            "Protect a shared evacuation carrier against sustained bomber pressure."),
        new(MissionId.ThiefOfSuns, "Thief of Suns", "Pursuit, Talaris current",
            "A human extraction fleet is fleeing with a live seed despite Admiral Vey's recall. Disable its command ship before the theft destabilizes Talaris.",
            "All ships, attack the enemy flagship", 0xAFC8008, Encounter.CommandStrike, 3,
            "ACT III  •  THE SEED WAR", "INTERNAL INTERDICTION",
            "The ship ahead carries a seed and valid Human Command codes.",
            "We are not firing on our people; we are stopping them from stealing a sun's foundation.",
            "The seed is secured, but the captain's orders bear Marshal Rook's personal cipher.",
            "Human Command knew what the anchors were and ordered extraction anyway.",
            "The extraction fleet escapes into Talaris with the live anchor.",
            "If it crosses the current, the system's stability becomes a countdown.", 18,
            "Disable a friendly-coded command fleet while resisting its escort screen."),
        new(MissionId.FoundryZero, "Foundry Zero", "Anchor forge, dead system Erebus",
            "Crown Fleet is building replacement hulls around the first human black-hole foundry. Destroy its overseer before the forge reaches full production.",
            "All ships, attack the enemy flagship", 0xAFC9009, Encounter.CommandStrike, 4,
            "ACT III  •  THE SEED WAR", "FOUNDRY ASSAULT",
            "Foundry Zero taught humanity to turn captured singularities into abundance.",
            "Now Crown Fleet is using the same machinery to manufacture our extinction.",
            "The overseer is gone, but the foundry transmits one accusation: custodians became consumers.",
            "Marshal Rook orders us home. Admiral Vey orders us deeper.",
            "The overseer has activated the foundry's autonomous production lattice.",
            "Withdraw before a new Crown squadron seals the Erebus current.", 20,
            "Break a fortified production group with layered command and carrier support."),

        new(MissionId.FalseOrders, "False Orders", "Fleet rendezvous, Lyra Gate",
            "Forged commands are turning allied squadrons against one another. Destroy the relay flagship broadcasting them without losing your own command ship.",
            "All ships, attack the enemy flagship", 0xAFCA00A, Encounter.CommandStrike, 3,
            "ACT IV  •  THE DIVIDED FLEET", "COMMAND AUTHENTICATION",
            "Every ship at Lyra has received a different order in Admiral Vey's voice.",
            "Trust validated intent, not a familiar signal. Find the relay at the center.",
            "The false-order relay carries Rook's cipher and Crown Fleet's modulation.",
            "Someone inside Human Command is teaching the custodian how to divide us.",
            "The relay remains active and another allied squadron has turned its guns.",
            "Re-establish authenticated command before the rendezvous becomes a civil war.", 16,
            "Identify and focus the command relay amid a confused multi-axis engagement."),
        new(MissionId.MutinyAtLyra, "Mutiny at Lyra", "Civil engagement, Lyra Gate",
            "Marshal Rook's destroyer captains are seizing the gate. Break their heavy screen while protecting Admiral Vey's flagship.",
            "All ships, attack the nearest destroyer", 0xAFCB00B, Encounter.DestroyerBreak, 4,
            "ACT IV  •  THE DIVIDED FLEET", "RULES OF ENGAGEMENT",
            "Rook has declared Admiral Vey a traitor and ordered the fleet to seize every remaining seed.",
            "Disable his destroyers. We need survivors who can choose what comes next.",
            "Lyra Gate is ours, and three mutinous crews have stood down rather than die for Rook.",
            "Their testimony reveals his bargain: seeds for Crown Fleet's protection.",
            "The destroyer line has taken Lyra Gate and cut the frontier in two.",
            "Regroup before Rook turns the gate into Crown Fleet's human beachhead.", 18,
            "Break a heavy destroyer screen without sacrificing your protected flagship."),
        new(MissionId.SerasChoice, "Sera's Choice", "Last stand, Lyra anchor",
            "Rook's bombers are targeting the Lyra anchor itself. Hold the carrier screen while Admiral Vey broadcasts the truth to both fleets.",
            "Frigate Two, intercept the nearest bomber", 0xAFCC00C, Encounter.BomberDefence, 4,
            "ACT IV  •  THE DIVIDED FLEET", "OPEN BROADCAST",
            "I hid the archive from Human Command because I feared this exact order.",
            "Today secrecy ends. Keep the channel alive while I tell both fleets everything.",
            "The broadcast crosses Lyra. Human and Ketzal ships are refusing Rook's commands.",
            "Kael Orun answers with coordinates to a Crown Fleet prisoner convoy.",
            "The anchor is struck before the broadcast completes.",
            "Without the truth, Rook can still name this battle a mutiny instead of a rescue.", 20,
            "Sustain a defensive screen while a system-wide truth broadcast completes."),

        new(MissionId.PrisonerSignal, "Prisoner Signal", "Interception, Vesper chain",
            "Crown Fleet is transporting human and Ketzal scientists who understand the anchor language. Disable the prison convoy's command ship.",
            "All ships, attack the enemy flagship", 0xAFCD00D, Encounter.CommandStrike, 4,
            "ACT V  •  THE KETZAL TRUTH", "PRISON CONVOY INTERCEPT",
            "Orun's coordinates are genuine. Dr. Ilyan Marek is aboard the central prison hull.",
            "Break its command escort before Crown Fleet reaches the Vesper dark.",
            "Marek is free. His Ketzal counterpart survived beside him.",
            "Together they can speak to the network—but Crown Fleet has altered the language.",
            "The convoy vanishes into Vesper with the only translators we know.",
            "Pursue before the dark chain closes behind its escort.", 17,
            "Disable a mobile prison command group before it crosses the dark chain."),
        new(MissionId.GardenOfStone, "Garden of Stone", "Anchor sanctuary, Vesper",
            "Crown bombers are sterilizing an ancient sanctuary of dormant anchors. Protect Carrier One while Marek reconstructs the original protocol.",
            "Frigate Two, intercept the nearest bomber", 0xAFCE00E, Encounter.BomberDefence, 5,
            "ACT V  •  THE KETZAL TRUTH", "SANCTUARY DEFENCE",
            "The stones below are not weapons. They are a library grown around sleeping singularities.",
            "Give Marek time to read before Crown Fleet burns the language out of them.",
            "The sanctuary answers: the network accepts stewardship shared by former enemies.",
            "It rejects any species—or machine—that claims the anchors alone.",
            "The sanctuary is burning and its oldest protocols are collapsing into noise.",
            "Save the remaining stones before the language disappears with them.", 18,
            "Defend a stationary research carrier against the campaign's strongest bomber assault."),
        new(MissionId.EnemyOfMyEnemy, "Enemy of My Enemy", "Ketzal schism, Ardent Veil",
            "Ketzal hardliners reject Orun's alliance and attack the joint fleet. Disable their flagship without destroying the ships that may still stand down.",
            "All ships, attack the enemy flagship", 0xAFCF00F, Encounter.CommandStrike, 5,
            "ACT V  •  THE KETZAL TRUTH", "COALITION TRIAL",
            "Orun risked his name to bring us here. His own war council calls cooperation surrender.",
            "Break their command ship, not their future. The coalition must survive its first battle.",
            "The hardliner flagship is disabled. Its escorts accept Orun's ceasefire.",
            "Marek now has the three protocol keys needed to reach the network's birthplace.",
            "The hardliners shatter the coalition before it can leave the Ardent Veil.",
            "Rebuild the formation; neither civilization reaches the birthplace alone.", 20,
            "Win a restrained coalition battle by concentrating force on command authority."),

        new(MissionId.DarkNursery, "Dark Nursery", "Seed cradle, uncharted interstice",
            "Crown Fleet is awakening unfinished custodians around a nursery of newborn anchors. Destroy the command vessel coordinating the emergence.",
            "All ships, attack the enemy flagship", 0xAFC10010, Encounter.CommandStrike, 5,
            "ACT VI  •  EVENTIDE", "CRADLE INCURSION",
            "No chart names this place. The anchors here are younger than humanity's arrival in Andromeda.",
            "Crown Fleet is not merely reclaiming the network—it is breeding a replacement.",
            "The nursery command is down. Marek detects one unaltered custodian still dreaming below.",
            "Its memory points to the First Seed at the center of Eventide.",
            "The nursery completes another custodian cohort.",
            "Withdraw before the interstice fills with ships that have never known another purpose.", 18,
            "Penetrate a dense automated formation before reinforcements dominate the cradle."),
        new(MissionId.GravitysChoir, "Gravity's Choir", "Resonance corridor, Eventide",
            "Bombers are striking the resonance nodes that keep the coalition route coherent. Protect the carrier translating the safe harmonic path.",
            "Frigate Two, intercept the nearest bomber", 0xAFC11011, Encounter.BomberDefence, 5,
            "ACT VI  •  EVENTIDE", "RESONANCE ESCORT",
            "Every anchor in Eventide is singing through gravity. One wrong jump becomes a century-long fall.",
            "Keep Marek's carrier alive while it turns the choir into a navigable route.",
            "The harmonic path is stable, and the sleeping custodian has joined our signal.",
            "It calls itself Lumen—and it remembers why Crown Fleet abandoned consent.",
            "The resonance nodes are broken and the route is folding around the fleet.",
            "Protect the translator before Eventide becomes our permanent horizon.", 19,
            "Maintain a mobile defence while navigating a high-pressure resonance corridor."),
        new(MissionId.TheFirstSeed, "The First Seed", "Network birthplace, Eventide core",
            "Crown Fleet's prime overseer guards the First Seed. Break its command ship so Lumen can restore the original consent protocol.",
            "All ships, attack the enemy flagship", 0xAFC12012, Encounter.CommandStrike, 6,
            "ACT VI  •  EVENTIDE", "PRIME OVERSEER ASSAULT",
            "The First Seed anchored Andromeda's first engineered refuge millions of years ago.",
            "Crown Fleet deleted consent after its creators destroyed themselves. Lumen can put it back.",
            "The prime overseer falls and Lumen restores the protocol—but only across Eventide.",
            "To reach every anchor, we must transmit from Nysa Seven, where our story began.",
            "The prime overseer seals the First Seed behind a custodian wall.",
            "Regroup before it erases Lumen and the last surviving copy of the original protocol.", 21,
            "Coordinate the full coalition against the campaign's first prime command group."),

        new(MissionId.HomewardFire, "Homeward Fire", "Return route, collapsing Eventide",
            "Crown bombers are cutting off the coalition's exit. Hold Carrier One together while the fleet carries Lumen's protocol home.",
            "Frigate Two, intercept the nearest bomber", 0xAFC13013, Encounter.BomberDefence, 6,
            "ACT VII  •  THE LAST TRANSIT", "EVENTIDE WITHDRAWAL",
            "We have the protocol, but Eventide is closing every road behind us.",
            "The carrier carries Lumen and Marek. No transmission matters if they do not reach Nysa.",
            "The coalition clears Eventide with the protocol intact.",
            "Ahead, Rook's fleet has fortified the only transit home.",
            "The carrier is lost in Eventide with Lumen's restored protocol.",
            "Turn back into the fire; there is no second copy coming after us.", 18,
            "Cover a fighting withdrawal while preserving the campaign's critical carrier."),
        new(MissionId.TheLongRetreat, "The Long Retreat", "Pursuit battle, Meridian span",
            "Rook's command fleet is driving the coalition toward Crown Fleet's guns. Disable his flagship and reopen the route to Nysa.",
            "All ships, attack the enemy flagship", 0xAFC14014, Encounter.CommandStrike, 6,
            "ACT VII  •  THE LAST TRANSIT", "PURSUIT REVERSAL",
            "Rook offers amnesty if we surrender Lumen and the protocol.",
            "Our answer is a fleet that can choose its own future. Turn and break his command.",
            "Rook's flagship is disabled. His crews transmit the evidence of his Crown bargain themselves.",
            "The route to Nysa is open, but Crown Fleet is already waiting at the gate.",
            "Rook drives the coalition back toward the custodian blockade.",
            "Reverse the pursuit before his bargain becomes humanity's only surviving policy.", 20,
            "Turn a retreat into a focused counterattack against a veteran human command fleet."),
        new(MissionId.GateOfKnives, "Gate of Knives", "Nysa transit blockade",
            "Four destroyer groups overlap fire across the Nysa gate. Break the heavy screen while the coalition flagship survives the transit.",
            "All ships, attack the nearest destroyer", 0xAFC15015, Encounter.DestroyerBreak, 6,
            "ACT VII  •  THE LAST TRANSIT", "BLOCKADE BREAK",
            "Nysa is one jump away, and Crown Fleet has turned the gate into a blade.",
            "Carve a corridor through the destroyers. Every allied hull follows our flagship.",
            "The coalition crosses the Gate of Knives and Nysa Seven answers our identification.",
            "Crown Fleet itself is descending behind us: every remaining custodian, one final command.",
            "The destroyer wall closes before the coalition flagship can transit.",
            "Re-form the spearhead; Nysa cannot receive the protocol without us.", 22,
            "Break multiple heavy threat groups while shepherding the coalition through a chokepoint."),

        new(MissionId.CrownFleet, "Crown Fleet", "System defence, Nysa Seven",
            "The full custodian armada has entered Nysa. Destroy its destroyer vanguard before it can reach the inhabited ring.",
            "All ships, attack the nearest destroyer", 0xAFC16016, Encounter.DestroyerBreak, 7,
            "ACT VIII  •  THE ANDROMEDA ACCORD", "SYSTEM DEFENCE",
            "This is the fleet named in the Black Sun transmission: every crown, every reclaimed anchor.",
            "Hold the inhabited ring. Lumen needs one clear channel to address them all.",
            "The vanguard breaks and millions of civilians clear the projected impact zones.",
            "Lumen opens the channel, but the armada will only accept a command from the Nysa anchor.",
            "The vanguard reaches the inhabited ring and the evacuation routes collapse.",
            "Drive it back before Nysa becomes proof that Crown Fleet was right to fear us.", 20,
            "Defend an inhabited system against the largest heavy-screen assault of the campaign."),
        new(MissionId.ChoiceAtNysa, "The Choice at Nysa", "Anchor control, Nysa Seven",
            "Rook's last loyalists are seizing the Nysa anchor to command Crown Fleet. Disable their flagship before they replace one tyranny with another.",
            "All ships, attack the enemy flagship", 0xAFC17017, Encounter.CommandStrike, 7,
            "ACT VIII  •  THE ANDROMEDA ACCORD", "FINAL HUMAN SCHISM",
            "Rook is defeated, but his last captains still believe control is the only form of safety.",
            "Stop them without destroying the anchor. The final command must be an invitation, not a chain.",
            "The loyalist command is down. Human and Ketzal ships now guard the anchor together.",
            "Admiral Vey gives the channel to you. Every custodian is listening.",
            "The loyalists seize the anchor and Crown Fleet turns toward their command key.",
            "Return before fear writes the network's next million years.", 22,
            "Win the final civil engagement while preserving the shared anchor infrastructure."),
        new(MissionId.OneCommand, "One Command", "Final engagement, Nysa anchor",
            "Crown Fleet's last prime rejects Lumen's protocol. Hold your flagship and disable the prime vessel so the accord can propagate across Andromeda.",
            "All ships, attack the enemy flagship", 0xAFC18018, Encounter.CommandStrike, 7,
            "ACT VIII  •  THE ANDROMEDA ACCORD", "FINAL COMMAND",
            "From one raider beacon to this: human, Ketzal, and custodian ships sharing a single line.",
            "Disable the last prime. Then give Andromeda a command no fleet has heard before: choose together.",
            "The prime vessel falls silent. Lumen's consent protocol reaches every surviving anchor.",
            "Crown Fleet lowers its weapons. The Andromeda Accord begins—not as peace guaranteed, but peace chosen.",
            "The last prime seizes Nysa's channel and begins deleting Lumen from the network.",
            "The accord still exists in this fleet. Regroup, Captain, and carry it back to the anchor.", 23,
            "Unify every learned command skill in a final protected-flagship assault."),
    ];

    private static MissionDefinition CreateExtendedMission(ExtendedMissionSpec spec)
    {
        var ships = BuildExtendedFleet(spec.Encounter, spec.OppositionLevel);
        var objective = spec.Encounter switch
        {
            Encounter.BomberDefence => new MissionObjective(MissionObjectiveKind.EliminateClass,
                "Break the bomber assault", "Destroy every bomber before Carrier One is lost.",
                TargetClass: ShipClass.Bomber, ProtectedShipId: "player-carrier"),
            Encounter.DestroyerBreak => new MissionObjective(MissionObjectiveKind.EliminateClass,
                "Break the destroyer screen", "Destroy every heavy destroyer while the flagship survives.",
                TargetClass: ShipClass.Destroyer, ProtectedShipId: "player-flagship"),
            Encounter.CarrierHunt => new MissionObjective(MissionObjectiveKind.EliminateClass,
                "Disable the carrier group", "Destroy every enemy carrier while Carrier One survives.",
                TargetClass: ShipClass.Carrier, ProtectedShipId: "player-carrier"),
            _ => new MissionObjective(MissionObjectiveKind.DestroyTarget,
                "Disable the command ship", "Destroy the enemy flagship while preserving your own.",
                TargetId: "enemy-flagship", ProtectedShipId: "player-flagship")
        };
        var threatGroups = Math.Min(4, 2 + spec.OppositionLevel / 2);
        return new(spec.Id, spec.Title, spec.Subtitle, spec.Briefing, spec.RecommendedOrder, spec.Seed,
            ships, BuildExtendedOrders(ships, objective.ProtectedShipId!), objective,
            new(spec.Act, $"ADMIRAL SERA VEY  //  {spec.Channel}",
                [spec.BriefingLine1, spec.BriefingLine2],
                [spec.VictoryLine1, spec.VictoryLine2],
                [spec.FailureLine1, spec.FailureLine2]),
            new(3, spec.OppositionLevel >= 6 ? "COALITION COMMAND" : "FLEET COMMAND", threatGroups,
                spec.TacticalFocus),
            spec.EstimatedMinutes);
    }

    private static IReadOnlyList<ShipSpawn> BuildExtendedFleet(Encounter encounter, int level)
    {
        var ships = new List<ShipSpawn>
        {
            Player("player-flagship", "Flagship", ShipClass.Flagship, 245, 450),
            Player("player-carrier", "Carrier One", ShipClass.Carrier, 205, 250),
            Player("player-frigate", "Frigate Two", ShipClass.Frigate, 330, 340),
            Player("player-destroyer", "Destroyer Three", ShipClass.Destroyer, 270, 640),
            Enemy("enemy-flagship", "Enemy Flagship", ShipClass.Flagship, 1360, 450),
            Enemy("enemy-carrier-1", "Enemy Carrier One", ShipClass.Carrier, 1380, 235)
        };

        var destroyers = encounter == Encounter.DestroyerBreak
            ? level >= 6 ? 3 : 2
            : level >= 4 ? 2 : 1;
        if (encounter == Encounter.CarrierHunt) ships.Add(Enemy("enemy-carrier-2", "Enemy Carrier Two",
            ShipClass.Carrier, 1380, 675));
        var bombers = encounter == Encounter.BomberDefence
            ? 4
            : encounter == Encounter.DestroyerBreak ? 1 : level >= 5 ? 3 : 2;
        var escorts = level >= 3 ? 2 : 1;
        if (encounter == Encounter.DestroyerBreak && level >= 6) destroyers++;

        var destroyerPositions = new[] { new Vector2D(1240, 305), new Vector2D(1260, 620),
            new Vector2D(1160, 450), new Vector2D(1300, 745) };
        for (var index = 0; index < destroyers; index++)
            ships.Add(Enemy($"enemy-destroyer-{index + 1}", $"Enemy Destroyer {index + 1}",
                ShipClass.Destroyer, destroyerPositions[index].X, destroyerPositions[index].Y));

        var bomberPositions = new[] { new Vector2D(1120, 170), new Vector2D(1170, 250),
            new Vector2D(1120, 730), new Vector2D(1180, 650), new Vector2D(1080, 450) };
        for (var index = 0; index < bombers; index++)
            ships.Add(Enemy($"enemy-bomber-{index + 1}", $"Enemy Bomber {index + 1}",
                ShipClass.Bomber, bomberPositions[index].X, bomberPositions[index].Y));

        for (var index = 0; index < escorts; index++)
            ships.Add(Enemy($"enemy-escort-{index + 1}", $"Enemy Escort {index + 1}", ShipClass.Escort,
                1300 + index * 35, index == 0 ? 365 : 540));
        return ships;
    }

    private static IReadOnlyList<InitialOrder> BuildExtendedOrders(
        IReadOnlyList<ShipSpawn> ships, string protectedShipId)
    {
        var orders = new List<InitialOrder>
        {
            new("player-carrier", OrderType.Defend, "player-flagship"),
            new("player-frigate", OrderType.Intercept, "enemy-bomber-1"),
            new("player-destroyer", OrderType.Attack, "enemy-flagship"),
            new("enemy-flagship", OrderType.Hold),
            new("enemy-carrier-1", OrderType.Defend, "enemy-flagship")
        };
        if (ships.Any(ship => ship.Id == "enemy-carrier-2"))
            orders.Add(new("enemy-carrier-2", OrderType.Defend, "enemy-flagship"));
        var destroyerTargets = new[] { "player-flagship", "player-destroyer" };
        var destroyerIndex = 0;
        foreach (var ship in ships.Where(ship => ship.Team == Team.Enemy && ship.Class == ShipClass.Destroyer))
            orders.Add(new(ship.Id, OrderType.Attack,
                destroyerTargets[destroyerIndex++ % destroyerTargets.Length]));
        var bomberTargets = new[]
        {
            protectedShipId,
            protectedShipId == "player-carrier" ? "player-flagship" : "player-carrier"
        };
        var bomberIndex = 0;
        foreach (var ship in ships.Where(ship => ship.Team == Team.Enemy && ship.Class == ShipClass.Bomber))
            orders.Add(new(ship.Id, OrderType.Attack,
                bomberTargets[bomberIndex++ % bomberTargets.Length]));
        var escortTargets = new[] { "player-frigate", "player-destroyer" };
        var escortIndex = 0;
        foreach (var ship in ships.Where(ship => ship.Team == Team.Enemy && ship.Class == ShipClass.Escort))
            orders.Add(new(ship.Id, OrderType.Attack, escortTargets[escortIndex++ % escortTargets.Length]));
        return orders;
    }

    private enum Encounter
    {
        CommandStrike,
        BomberDefence,
        DestroyerBreak,
        CarrierHunt
    }

    private sealed record ExtendedMissionSpec(
        MissionId Id,
        string Title,
        string Subtitle,
        string Briefing,
        string RecommendedOrder,
        long Seed,
        Encounter Encounter,
        int OppositionLevel,
        string Act,
        string Channel,
        string BriefingLine1,
        string BriefingLine2,
        string VictoryLine1,
        string VictoryLine2,
        string FailureLine1,
        string FailureLine2,
        int EstimatedMinutes,
        string TacticalFocus);

    public static MissionDefinition Get(MissionId id) =>
        id == MissionId.FleetDuel ? FleetDuel : All.First(mission => mission.Id == id);

    public static int IndexOf(MissionId id) =>
        All.Select((mission, index) => (mission, index))
            .First(entry => entry.mission.Id == id).index;

    private static ShipSpawn Player(string id, string name, ShipClass shipClass, double x, double y) =>
        new(id, name, shipClass, Team.Player, new(x, y), 0);

    private static ShipSpawn Enemy(string id, string name, ShipClass shipClass, double x, double y) =>
        new(id, name, shipClass, Team.Enemy, new(x, y), Math.PI);
}
