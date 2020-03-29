module.exports = {
    root: true,
    env: {
        node: true
    },
    extends: [
        "plugin:vue/essential",
        "eslint:recommended",
        "prettier",
        "@vue/typescript/recommended",
        "@vue/prettier",
        "@vue/prettier/@typescript-eslint"
    ],
    parserOptions: {
        ecmaVersion: 2020
    },
    rules: {
        "no-console": process.env.NODE_ENV === "production" ? "error" : "off",
        "no-debugger": process.env.NODE_ENV === "production" ? "error" : "off",
        '@typescript-eslint/no-empty-function': 0,
        "@typescript-eslint/interface-name-prefix": 0,
        'prettier/prettier': 0,
        'no-constant-condition': 0,
        'prefer-const': 0,
        '@typescript-eslint/no-use-before-define': 0,
        "@typescript-eslint/consistent-type-assertions": 0
    },
    overrides: [
        {
            files: [
                "**/__tests__/*.{j,t}s?(x)",
                "**/tests/unit/**/*.spec.{j,t}s?(x)"
            ],
            env: {
                jest: true
            }
        }
    ]
};
