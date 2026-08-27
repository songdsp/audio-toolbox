/*
    Authors the EventTracer test fixture inside an FMOD Studio project.

    Run headlessly via build-trace-fixture.ps1, which prepends a FIXTURE config
    object holding absolute paths. Every event exists to make exactly one
    PlaybackOutcome reproducible on demand; see Documentation~/EventTracer.md.

    Re-running is safe: events that already exist are reused and reconfigured
    rather than duplicated, so the script doubles as "put the fixture back the
    way it should be" after someone edits it in the UI.
*/

/*
    Stealing modes as the project file stores them, which is NOT the order the Studio
    dropdown lists them in (Oldest, Furthest, Quietest, Virtualize, None). Furthest was
    added to Studio later and appended to the stored enum rather than inserted, so a
    script that trusts the dropdown order silently authors the wrong behaviour: 4 reads
    as None but steals, and 3 reads as Virtualize but refuses. Verified against the
    engine by FmodCallbackDiagnostics.
*/
var STEAL_OLDEST = 0;
var STEAL_QUIETEST = 1;
var STEAL_VIRTUALIZE = 2;
var STEAL_NONE = 3;
var STEAL_FURTHEST = 4;

var FOLDER_NAME = "AudioToolboxTrace";
var BANK_NAME = "TraceFixture";

/*
    A global parameter for the context capture tests. Global parameters are flat and
    project-wide - there are no folders to hide one in - so the name carries its own
    namespace to keep it out of the way of whatever the project already has.
*/
var GLOBAL_PARAMETER_NAME = "AudioToolboxTraceTension";

function log(message) {
    console.log("[trace-fixture] " + message);
}

function findByName(entityName, name) {
    var instances = studio.project.model[entityName].findInstances();
    for (var i = 0; i < instances.length; i++) {
        if (instances[i].name === name) {
            return instances[i];
        }
    }
    return null;
}

function findEvent(folder, name) {
    var events = studio.project.model.Event.findInstances();
    for (var i = 0; i < events.length; i++) {
        if (events[i].name === name && events[i].folder && events[i].folder.id === folder.id) {
            return events[i];
        }
    }
    return null;
}

function ensureFolder() {
    var existing = findByName("EventFolder", FOLDER_NAME);
    if (existing) {
        return existing;
    }

    var folder = studio.project.create("EventFolder");
    folder.name = FOLDER_NAME;
    folder.folder = studio.project.workspace.masterEventFolder;
    log("created event folder " + FOLDER_NAME);
    return folder;
}

function ensureBank() {
    var existing = findByName("Bank", BANK_NAME);
    if (existing) {
        return existing;
    }

    var bank = studio.project.create("Bank");
    bank.name = BANK_NAME;
    bank.folder = studio.project.workspace.masterBankFolder;
    log("created bank " + BANK_NAME);
    return bank;
}

/*
    A project-wide parameter, which in the project model is a ParameterPreset owning a
    GameParameter with isGlobal set. The preset is what appears in the Parameters
    browser; the GameParameter underneath it is where every property that matters lives.
*/
function ensureGlobalParameter() {
    var preset = findByName("ParameterPreset", GLOBAL_PARAMETER_NAME);

    if (!preset) {
        preset = studio.project.create("ParameterPreset");
        preset.name = GLOBAL_PARAMETER_NAME;
        preset.folder = studio.project.workspace.masterParameterPresetFolder;
        log("created parameter preset " + GLOBAL_PARAMETER_NAME);
    }

    var parameter = preset.parameter;

    if (!parameter) {
        parameter = studio.project.create("GameParameter");
        preset.parameter = parameter;
    }

    parameter.isGlobal = true;
    parameter.minimum = 0;
    parameter.maximum = 1;
    parameter.initialValue = 0;

    log("  " + GLOBAL_PARAMETER_NAME + ": isGlobal=" + parameter.isGlobal +
        " range=" + parameter.minimum + ".." + parameter.maximum);
    return preset;
}

function importAudio(path) {
    var name = path.replace(/\\/g, "/").split("/").pop();
    var existing = studio.project.model.AudioFile.findInstances();
    for (var i = 0; i < existing.length; i++) {
        if (existing[i].assetPath === name) {
            return existing[i];
        }
    }

    var imported = studio.project.importAudioFile(path);
    if (!imported) {
        throw new Error("importAudioFile failed for " + path);
    }
    log("imported " + imported.assetPath + " (" + imported.length.toFixed(3) + "s)");
    return imported;
}

/*
    A SingleSound placed on the master track's timeline. 'owner' is not the
    relationship to use here - it expects a MultiSound, because it models a
    sound nested inside a playlist. A timeline instrument is instead attached to
    the track it draws on and to the timeline's module list.
*/
function setTimelineSound(event, audioFile) {
    var timeline = event.timeline;
    var modules = timeline.relationships.modules.destinations;

    var sound = null;
    for (var i = 0; i < modules.length; i++) {
        if (modules[i].isOfExactType("SingleSound")) {
            sound = modules[i];
            break;
        }
    }

    if (!sound) {
        sound = studio.project.create("SingleSound");
        sound.audioTrack = event.masterTrack;
        timeline.relationships.modules.add(sound);
    }

    sound.audioFile = audioFile;
    sound.start = 0;
    sound.length = audioFile.length;
    return sound;
}

function setSpatialiser(event, minDistance, maxDistance) {
    var chain = event.mixer.masterBus.effectChain;
    var effects = chain.relationships.effects.destinations;

    var spatialiser = null;
    for (var i = 0; i < effects.length; i++) {
        if (effects[i].isOfExactType("SpatialiserEffect")) {
            spatialiser = effects[i];
            break;
        }
    }

    if (!spatialiser) {
        spatialiser = studio.project.create("SpatialiserEffect");
        chain.relationships.effects.add(spatialiser);
    }

    // Without overrideRange the min/max written here are ignored in favour of
    // the values derived from the event's contents, which is not deterministic
    // enough to assert a distance against.
    spatialiser.overrideRange = true;
    spatialiser.minimumDistance = minDistance;
    spatialiser.maximumDistance = maxDistance;
    return spatialiser;
}

function ensureEvent(folder, bank, audioFile, spec) {
    var event = findEvent(folder, spec.name);
    if (!event) {
        event = studio.project.create("Event");
        event.name = spec.name;
        event.folder = folder;
        log("created event " + spec.name);
    }

    event.note = spec.note;
    setTimelineSound(event, audioFile);

    /*
        The event macro drawer's "Max Instances" and "Stealing" live on
        EventAutomatableProperties as maxVoices and voiceStealing. EventMixerMaster has
        properties named maxInstances and instanceStealing too, which is the trap: those
        are the mixer bus's own instance limit, they set and save without complaint, and
        the built bank then behaves as though nothing was configured at all.
    */
    var macros = event.automatableProperties;
    macros.maxVoices = spec.maxInstances;
    macros.voiceStealing = spec.stealing;

    if (spec.spatial) {
        setSpatialiser(event, spec.minDistance, spec.maxDistance);
    }

    var banks = event.relationships.banks.destinations;
    var assigned = false;
    for (var i = 0; i < banks.length; i++) {
        if (banks[i].id === bank.id) {
            assigned = true;
            break;
        }
    }
    if (!assigned) {
        event.relationships.banks.add(bank);
    }

    log("  " + spec.name +
        ": maxVoices=" + macros.maxVoices +
        " stealing=" + macros.voiceStealing +
        " is3D=" + event.is3D());
    return event;
}

var SPECS = [
    {
        name: "Basic2D",
        note: "Plain 2D event. Baseline for Started, and long enough that a stop mid-flight is unambiguously StoppedEarly.",
        maxInstances: 65,
        stealing: STEAL_OLDEST,
        spatial: false,
    },
    {
        name: "Spatial3D",
        note: "3D with an explicit 1-10m range. Placed past 10m it is inaudible, which is how the virtual voice system is provoked without touching instance limits.",
        maxInstances: 65,
        stealing: STEAL_OLDEST,
        spatial: true,
        minDistance: 1,
        maxDistance: 10,
    },
    {
        name: "MaxOneReject",
        note: "max instances 1, stealing None. A second post is refused outright: created, never started, destroyed. That is Rejected.",
        maxInstances: 1,
        stealing: STEAL_NONE,
        spatial: false,
    },
    {
        name: "MaxOneSteal",
        note: "max instances 1, stealing Oldest. A second post stops the first without anyone asking it to. That is Stolen.",
        maxInstances: 1,
        stealing: STEAL_OLDEST,
        spatial: false,
    },
    {
        name: "MaxOneVirtualize",
        note: "max instances 1, stealing Virtualize. A second post plays but produces no output.",
        maxInstances: 1,
        stealing: STEAL_VIRTUALIZE,
        spatial: false,
    },
];

log("project: " + FIXTURE.projectPath);

var folder = ensureFolder();
var bank = ensureBank();
ensureGlobalParameter();
var audioFile = importAudio(FIXTURE.longWavPath);

for (var i = 0; i < SPECS.length; i++) {
    ensureEvent(folder, bank, audioFile, SPECS[i]);
}

studio.project.save();
log("saved");

if (!studio.project.build({ banks: BANK_NAME })) {
    throw new Error("bank build failed");
}
log("built bank " + BANK_NAME);
log("done");
