# Firebase Setup in DTA Training Companion

To set up dynamic real-time database persistence and Google Cloud Auth, perform the following steps:

1. **Provision Firebase**:
   Run the `set_up_firebase` tool or create a project on the [Firebase Console](https://console.firebase.google.com).
   - Enable **Firestore Database** in test or locked rules.
   - Enable **Authentication** and activate the Google Login provider.

2. **Add Configuration**:
   Create a file named `firebase-applet-config.json` in the root of your workspace containing your keys:
   ```json
   {
     "apiKey": "YOUR_API_KEY",
     "authDomain": "YOUR_PROJECT.firebaseapp.com",
     "projectId": "YOUR_PROJECT",
     "storageBucket": "YOUR_PROJECT.appspot.com",
     "messagingSenderId": "SENDER_ID",
     "appId": "APP_ID",
     "firestoreDatabaseId": "(default)"
   }
   ```

3. **Deploy Security Rules**:
   Run `deploy_firebase` or replace your console security rules with the contents of the generated `firestore.rules` file in this workspace.

4. **Install ESLint Rule Guards**:
   ```bash
   npm install --save-dev @firebase/eslint-plugin-security-rules
   ```
   Add standard rules config into your flat ESLint config file to prevent security regressions during edits.
