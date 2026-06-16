import { initializeApp, type FirebaseApp } from "firebase/app";
import {
  getAuth,
  GoogleAuthProvider,
  signInWithEmailAndPassword,
  signInWithPopup,
  createUserWithEmailAndPassword,
  type Auth,
} from "firebase/auth";
import { getFirestore, doc, setDoc, type Firestore } from "firebase/firestore";

export type FirebaseBundle = { app: FirebaseApp; auth: Auth; db: Firestore };

let bundle: FirebaseBundle | null = null;

export async function initFirebase(): Promise<FirebaseBundle | null> {
  if (bundle) return bundle;

  const res = await fetch("/api/firebase/config");
  const cfg = await res.json();
  if (!cfg.enabled) return null;

  const app = initializeApp({
    apiKey: cfg.apiKey,
    authDomain: cfg.authDomain,
    projectId: cfg.projectId,
    storageBucket: cfg.storageBucket,
    messagingSenderId: cfg.messagingSenderId,
    appId: cfg.appId,
  });

  bundle = { app, auth: getAuth(app), db: getFirestore(app) };
  return bundle;
}

export async function signInWithGoogle() {
  const fb = await initFirebase();
  if (!fb) throw new Error("Firebase not configured — add firebase-applet-config.json");
  const provider = new GoogleAuthProvider();
  const cred = await signInWithPopup(fb.auth, provider);
  await upsertUserDoc(fb.db, cred.user.uid, cred.user.email || "", cred.user.displayName || "Player", "student");
  return cred.user;
}

export async function signInWithEmail(email: string, password: string, role: "student" | "teacher" = "student") {
  const fb = await initFirebase();
  if (!fb) throw new Error("Firebase not configured");
  try {
    const cred = await signInWithEmailAndPassword(fb.auth, email, password);
    await upsertUserDoc(fb.db, cred.user.uid, email, cred.user.displayName || email, role);
    return cred.user;
  } catch {
    const cred = await createUserWithEmailAndPassword(fb.auth, email, password);
    await upsertUserDoc(fb.db, cred.user.uid, email, email.split("@")[0], role);
    return cred.user;
  }
}

async function upsertUserDoc(
  db: Firestore,
  uid: string,
  email: string,
  displayName: string,
  role: "student" | "teacher",
) {
  await setDoc(
    doc(db, "users", uid),
    {
      uid,
      email,
      displayName,
      role,
      isLinked: false,
      updatedAt: new Date().toISOString(),
    },
    { merge: true },
  );
}
